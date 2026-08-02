using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

// Yarn integration Phase 1 acceptance (docs/plans/npc-dialogue-yarn.md): Yarn
// source compiles into the same DialogueGraph the editor and DialogueRuntime
// already use, the graph writes back out as Yarn, and the two directions agree.
// Also guards the committed .yarn content and every committed .dialogue.json,
// which must survive a round trip through Yarn unchanged — that is what makes
// converting a conversation to .yarn a safe, reviewable diff.
public class YarnDialogueTests
{
    private const string Greeter = @"
title: hello
---
$npc: New around the station?
-> Where's the medbay?
    <<jump medbay>>
-> Here, take this cube off my hands <<if has_item(1)>>
    <<take_item 1>>
    $npc: I'll see it gets where it's going.
-> Just passing through
    <<jump farewell>>
===

title: medbay
---
<<give_item 4 1>>
$npc: Two decks down. Here, a medkit.
<<jump farewell>>
===

title: farewell
---
$npc: Watch your step out there.
===
";

    private static DialogueGraph Compile(string source, out List<string> problems)
    {
        problems = new List<string>();
        return YarnGraphCompiler.Compile("test.dialogue", source, problems);
    }

    private static DialogueGraph CompileClean(string source)
    {
        var graph = Compile(source, out var problems);
        Assert.Empty(problems);
        return graph;
    }

    [Fact]
    public void CompilesNodesLinesAndChoices()
    {
        var graph = CompileClean(Greeter);

        Assert.Equal("test.dialogue", graph.Id);
        Assert.Equal(DialogueSourceFormat.Yarn, graph.SourceFormat);
        Assert.Equal("hello", graph.EntryNodeId);

        var hello = graph.GetNode("hello");
        Assert.Equal(DialogueGraph.SpeakerToken, hello.Speaker);
        Assert.Equal("New around the station?", hello.Text);
        Assert.Equal(3, hello.Choices.Count);

        Assert.Equal("Where's the medbay?", hello.Choices[0].Label);
        Assert.Equal("medbay", hello.Choices[0].NextNodeId);
        Assert.Null(hello.Choices[0].Visible);
        Assert.Null(hello.Choices[0].Effects);

        // A gated option keeps its condition, its own effect, and the reply
        // nested under it becomes a node of its own.
        var cube = hello.Choices[1];
        Assert.Equal("has_item:1", cube.Visible.ToToken());
        Assert.Equal(new[] { "take_item:1" }, cube.Effects.Select(e => e.ToToken()));
        var reply = graph.GetNode(cube.NextNodeId);
        Assert.Equal("I'll see it gets where it's going.", reply.Text);
        // Nothing follows the option group, so the reply ends the conversation.
        Assert.True(string.IsNullOrEmpty(reply.NextNodeId));

        Assert.Equal("farewell", hello.Choices[2].NextNodeId);
    }

    [Fact]
    public void CommandsAboveALineBecomeItsOnShownEffects()
    {
        var graph = CompileClean(Greeter);
        var medbay = graph.GetNode("medbay");

        Assert.Equal(new[] { "give_item:4:1" }, medbay.OnShownEffects.Select(e => e.ToToken()));
        Assert.Equal("farewell", medbay.NextNodeId);
    }

    [Fact]
    public void CommandsBelowTheLastLineAttachToIt()
    {
        var graph = CompileClean(@"
title: done
---
$npc: Yes, that's the one.
<<take_item 1>>
<<set_quest 1 Success>>
===
");
        var node = graph.GetNode("done");
        Assert.Equal(new[] { "take_item:1", "set_quest:1:Success" }, node.OnShownEffects.Select(e => e.ToToken()));
    }

    [Fact]
    public void IfChainCompilesToARouterNode()
    {
        var graph = CompileClean(@"
title: entry
---
<<if quest_state(1, ""Success"")>>
    <<jump done>>
<<elseif quest_state(1, ""InProgress"")>>
    <<jump nagging>>
<<else>>
    <<jump offer>>
<<endif>>
===

title: done
---
$npc: Thanks again.
===

title: nagging
---
$npc: Any sign of it?
===

title: offer
---
$npc: Care to help?
===
");
        var entry = graph.GetNode("entry");
        Assert.Equal(3, entry.Branches.Count);
        Assert.Equal("quest_state:1:Success", entry.Branches[0].When.ToToken());
        Assert.Equal("done", entry.Branches[0].ToNodeId);
        Assert.Equal("quest_state:1:InProgress", entry.Branches[1].When.ToToken());
        Assert.Equal("nagging", entry.Branches[1].ToNodeId);
        // <<else>> is the unconditional last branch.
        Assert.Null(entry.Branches[2].When);
        Assert.Equal("offer", entry.Branches[2].ToNodeId);
        Assert.Null(entry.Text);
    }

    [Fact]
    public void AnIfWithoutAnElseFallsThroughToWhatFollowsEndif()
    {
        var graph = CompileClean(@"
title: entry
---
<<if has_item(1)>>
    <<jump turnin>>
<<endif>>
$npc: Any sign of my cube?
===

title: turnin
---
$npc: You found it!
===
");
        var entry = graph.GetNode("entry");
        Assert.Single(entry.Branches);
        Assert.Equal("turnin", entry.Branches[0].ToNodeId);
        // The reminder line is the router's fallback, and a node of its own.
        var fallback = graph.GetNode(entry.NextNodeId);
        Assert.Equal("Any sign of my cube?", fallback.Text);
    }

    [Fact]
    public void StopEndsTheConversationAndOptionsCanRunEffectsOnly()
    {
        var graph = CompileClean(@"
title: demand
---
$npc: Hand it over, or I take it.
-> Try and take it
    <<start_battle>>
    <<stop>>
-> Fine, take it
    <<jump complete>>
===

title: complete
---
$npc: You have my thanks.
===
");
        var demand = graph.GetNode("demand");
        Assert.Equal(new[] { "start_battle" }, demand.Choices[0].Effects.Select(e => e.ToToken()));
        Assert.True(string.IsNullOrEmpty(demand.Choices[0].NextNodeId));
        Assert.Equal("complete", demand.Choices[1].NextNodeId);
    }

    [Fact]
    public void NarrationLinesHaveNoSpeakerAndEscapedColonsSurvive()
    {
        var graph = CompileClean(@"
title: start
---
The lift shudders to a halt.
Hale\: a name you have heard before.
===
");
        var first = graph.GetNode("start");
        Assert.Null(first.Speaker);
        Assert.Equal("The lift shudders to a halt.", first.Text);
        var second = graph.GetNode(first.NextNodeId);
        Assert.Null(second.Speaker);
        Assert.Equal("Hale: a name you have heard before.", second.Text);
    }

    [Fact]
    public void TheEntryHeaderPicksTheStartingNode()
    {
        var graph = CompileClean(@"
title: later
---
$npc: Second in the file.
===

title: first_played
entry: true
---
$npc: But this one starts the conversation.
<<jump later>>
===
");
        Assert.Equal("first_played", graph.EntryNodeId);
    }

    [Fact]
    public void CommentsAndLineTagsAreNotDialogueText()
    {
        var graph = CompileClean(@"
title: start
---
// a note to the writer
$npc: Watch your step. #line:0a1b
===
");
        Assert.Equal("Watch your step.", graph.GetNode("start").Text);
    }

    [Theory]
    // Unknown verbs, unsupported Yarn features, and broken structure are all
    // reported with the source line rather than silently mistranslated.
    [InlineData("<<teleport_player 3>>", "unknown command")]
    [InlineData("<<set $met = true>>", "not supported")]
    [InlineData("<<give_item 999>>", "no item with id 999")]
    [InlineData("<<jump nowhere>>", "doesn't have")]
    [InlineData("<<if $met_hale>>\n<<endif>>", "Yarn variables aren't supported")]
    [InlineData("<<if visited(\"x\")>>\n<<endif>>", "unknown condition 'visited'")]
    [InlineData("<<if has_item(1) && party_has_room()>>\n<<endif>>", "isn't supported")]
    [InlineData("<<if has_item(1) and party_has_room()>>\n<<endif>>", "'and' isn't supported")]
    [InlineData("<<if credits() gte 200>>\n<<endif>>", "symbolically")]
    [InlineData("<<if credits() + 1 >= 200>>\n<<endif>>", "arithmetic")]
    [InlineData("<<if credits() >= >>\n<<endif>>", "missing one side")]
    [InlineData("<<if credits() >= two hundred>>\n<<endif>>", "isn't a literal")]
    [InlineData("<<if not>>\n<<endif>>", "negates nothing")]
    [InlineData("<<if credits()>>\n<<endif>>", "compared to a value")]
    [InlineData("<<if has_item(1)>>\n$npc: Hi.", "no matching <<endif>>")]
    [InlineData("<<give_item 4>>", "no line of dialogue to run on")]
    [InlineData("-> An option with no line\n    <<stop>>", "must follow a line")]
    [InlineData("$npc: Hi.\n<<jump", "unterminated command")]
    public void ReportsWhatItCannotCompile(string body, string expected)
    {
        Compile($"title: start\n---\n{body}\n===\n", out var problems);
        Assert.Contains(problems, p => p.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ComparisonsAndNegationCompileAndWriteBackOut()
    {
        // dialogue-state-conditions Phase 1: a query compared to a literal, and a
        // negated predicate. Both are written the way real Yarn writes them — a
        // function call in an expression — so the file stays valid upstream.
        var source = @"
title: toll
---
<<if credits() >= 200>>
    <<jump berth>>
<<elseif not flag(""warned"")>>
    <<jump warn>>
<<else>>
    <<jump turned_away>>
<<endif>>
===

title: berth
---
$npc: Clamps are off.
-> Buy a spare cell <<if item_count(1) == 0>>
    <<stop>>
===

title: warn
---
<<set_flag warned>>
$npc: Two hundred, or you drift.
===

title: turned_away
---
$npc: Drift, then.
===
";
        var graph = CompileClean(source);
        var router = graph.GetNode("toll");
        Assert.Equal("credits:>=:200", router.Branches[0].When.ToToken());
        Assert.Equal("!flag:warned", router.Branches[1].When.ToToken());
        Assert.Equal("item_count:1:==:0", graph.GetNode("berth").Choices[0].Visible.ToToken());

        // The writer's spelling parses back to the same tokens.
        var problems = new List<string>();
        var yarn = YarnGraphWriter.Write(graph, problems);
        Assert.Empty(problems);
        Assert.Contains("credits() >= 200", yarn);
        Assert.Contains("not flag(\"warned\")", yarn);
        Assert.Contains("item_count(1) == 0", yarn);
        AssertSameGraph(graph, YarnGraphCompiler.Compile(graph.Id, yarn, problems));
        Assert.Empty(problems);
    }

    [Fact]
    public void ANegatedComparisonFoldsIntoTheOperator()
    {
        // Yarn's unary `not` binds tighter than a comparison, so writing
        // `not credits() >= 200` back out would read upstream as
        // `(not credits()) >= 200`. Storing the inverted operator says the same
        // thing and survives the trip through the file.
        var graph = CompileClean(@"
title: start
---
<<if not credits() >= 200>>
    <<jump broke>>
<<endif>>
$npc: Welcome aboard.
===

title: broke
---
$npc: Come back with money.
===
");
        Assert.Equal("credits:<:200", graph.GetNode("start").Branches[0].When.ToToken());

        // And a hand-built token that still carries the '!' is written the same
        // unambiguous way, while a negated predicate keeps its `not`.
        Assert.Equal("credits() < 200", YarnGraphWriter.ConditionText(ConditionRef.Parse("!credits:>=:200")));
        Assert.Equal("not flag(\"met_hale\")", YarnGraphWriter.ConditionText(ConditionRef.Parse("!flag:met_hale")));
    }

    [Fact]
    public void AnElidedOperatorNormalizesToTheDefaultComparison()
    {
        // party_size(2) and stat("Charisma", 12) are how the vocabulary was
        // written before there was an operator; they mean ">=" and normalize to
        // it, so the writer only ever emits one form.
        var graph = CompileClean(@"
title: start
---
<<if party_size(2)>>
    <<jump crew>>
<<endif>>
$npc: Just you, then.
-> Charm them <<if stat(""Charisma"", 12)>>
    <<stop>>
===

title: crew
---
$npc: Quite the crew.
===
");
        Assert.Equal("party_size:>=:2", graph.GetNode("start").Branches[0].When.ToToken());
        var line = graph.GetNode(graph.GetNode("start").NextNodeId);
        Assert.Equal("stat:Charisma:>=:12", line.Choices[0].Visible.ToToken());
    }

    [Fact]
    public void AProblemStillYieldsAPlayableGraph()
    {
        // A dangling jump is reported, and the rest of the conversation still
        // compiles — the catalog would rather load a flawed script than lose it.
        var graph = Compile("title: start\n---\n$npc: Hello.\n<<jump nowhere>>\n===\n", out var problems);
        Assert.NotEmpty(problems);
        Assert.Equal("Hello.", graph.GetNode("start").Text);
        Assert.True(string.IsNullOrEmpty(graph.GetNode("start").NextNodeId));
    }

    [Fact]
    public void ACompiledConversationPlaysThroughTheRuntime()
    {
        var graph = CompileClean(@"
title: entry
---
<<if quest_state(1, ""InProgress"")>>
    <<jump turnin>>
<<endif>>
$npc: Care to find my cube?
-> I'll find it
    <<set_quest 1 InProgress>>
    $npc: Much obliged.
-> Not now
    <<stop>>
===

title: turnin
---
<<take_item 1>>
<<set_quest 1 Success>>
$npc: You have my thanks.
===
");
        Assert.Empty(DialogueValidation.Validate(graph).Where(i => i.Severity == DialogueSeverity.Error));

        var state = new GameState();
        var context = new DialogueContext { State = state, SpeakerName = "Hale" };

        // Fresh state: the router falls through to the offer, and accepting it
        // runs the choice's effect through the same DialogueEffects dispatcher.
        var line = DialogueRuntime.Compile(graph, context);
        Assert.Equal("Hale", line.Speaker);
        Assert.Equal("Care to find my cube?", line.Text);
        line.Choices[0].Action();
        Assert.Equal(QUESTSUCCESSSTATE.InProgress, state.GetQuestState(1));

        // With the quest running, the router now takes the turn-in branch, whose
        // on-shown effects complete it.
        state.Inventory.Add(1, 1);
        var turnin = DialogueRuntime.Compile(graph, context);
        Assert.Equal("You have my thanks.", turnin.Text);
        turnin.OnShown();
        Assert.Equal(QUESTSUCCESSSTATE.Success, state.GetQuestState(1));
        Assert.Equal(0u, state.Inventory.CountOf(1));
    }

    [Fact]
    public void GraphsWriteBackOutAsYarnAndCompileToTheSameGraph()
    {
        var original = CompileClean(Greeter);
        var problems = new List<string>();
        var source = YarnGraphWriter.Write(original, problems);
        Assert.Empty(problems);

        var rewritten = Compile(source, out var reproblems);
        Assert.Empty(reproblems);
        AssertSameGraph(original, rewritten);
    }

    [Fact]
    public void ConversationsCrossBetweenTheTwoFormatsUnchanged()
    {
        // .dialogue.json is still a supported *input*, and saving one in the
        // editor converts it to Yarn, so a graph has to survive the trip in both
        // directions: this is what makes that conversion a safe diff.
        foreach (var (name, graph) in CommittedYarn())
        {
            var viaJson = DialogueSerialization.FromJson(DialogueSerialization.ToJson(graph));
            AssertSameGraph(graph, viaJson, name);

            var problems = new List<string>();
            var viaYarn = YarnGraphCompiler.Compile(
                graph.Id, YarnGraphWriter.Write(viaJson, problems), problems);
            Assert.Empty(problems.Select(p => $"{name}: {p}"));
            AssertSameGraph(graph, viaYarn, name);
        }
    }

    [Fact]
    public void EveryCommittedYarnConversationCompilesAndValidates()
    {
        foreach (var (name, graph) in CommittedYarn())
        {
            var issues = DialogueValidation.Validate(graph);
            Assert.Empty(issues.Select(i => $"{name}: {i}"));

            // And it survives the editor's save path (graph -> .yarn -> graph).
            var problems = new List<string>();
            var rewritten = YarnGraphCompiler.Compile(graph.Id, YarnGraphWriter.Write(graph, problems), problems);
            Assert.Empty(problems.Select(p => $"{name}: {p}"));
            AssertSameGraph(graph, rewritten, name);
        }
    }

    // Every conversation committed under Resources/Dialogue as Yarn, compiled.
    // A compile problem fails here rather than in each caller.
    private static List<(string name, DialogueGraph graph)> CommittedYarn()
    {
        var files = Directory.GetFiles(DialogueDirectory(), "*" + YarnGraphCompiler.FileSuffix)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(files);
        var loaded = new List<(string, DialogueGraph)>();
        foreach (var path in files)
        {
            var name = Path.GetFileName(path);
            var problems = new List<string>();
            var graph = YarnGraphCompiler.Compile(
                YarnGraphCompiler.IdFromFileName(name), File.ReadAllText(path), problems);
            Assert.Empty(problems.Select(p => $"{name}: {p}"));
            loaded.Add((name, graph));
        }
        return loaded;
    }

    private static string DialogueDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Resources", "Dialogue");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new DirectoryNotFoundException("Could not locate Resources/Dialogue above the test assembly.");
    }

    // Structural equality: same entry, same node ids, and every node's speaker,
    // text, effects, links, choices, and branches identical. Null and empty read
    // the same (JSON omits nulls, Yarn has no way to write one).
    private static void AssertSameGraph(DialogueGraph expected, DialogueGraph actual, string where = "graph")
    {
        Assert.Equal(expected.EntryNodeId, actual.EntryNodeId);
        Assert.Equal(
            expected.Nodes.Select(n => n.Id).OrderBy(id => id, StringComparer.Ordinal),
            actual.Nodes.Select(n => n.Id).OrderBy(id => id, StringComparer.Ordinal));

        foreach (var node in expected.Nodes)
        {
            var other = actual.GetNode(node.Id);
            Assert.NotNull(other);
            Assert.Equal(Text(node.Speaker), Text(other.Speaker));
            Assert.Equal(Text(node.Text), Text(other.Text));
            Assert.Equal(Tokens(node.OnShownEffects), Tokens(other.OnShownEffects));
            Assert.Equal(Text(node.NextNodeId), Text(other.NextNodeId));

            Assert.Equal(Count(node.Branches), Count(other.Branches));
            for (var i = 0; i < Count(node.Branches); i++)
            {
                Assert.Equal(Token(node.Branches[i].When), Token(other.Branches[i].When));
                Assert.Equal(Text(node.Branches[i].ToNodeId), Text(other.Branches[i].ToNodeId));
            }

            Assert.Equal(Count(node.Choices), Count(other.Choices));
            for (var i = 0; i < Count(node.Choices); i++)
            {
                Assert.Equal(Text(node.Choices[i].Label), Text(other.Choices[i].Label));
                Assert.Equal(Token(node.Choices[i].Visible), Token(other.Choices[i].Visible));
                Assert.Equal(Tokens(node.Choices[i].Effects), Tokens(other.Choices[i].Effects));
                Assert.Equal(Text(node.Choices[i].NextNodeId), Text(other.Choices[i].NextNodeId));
            }
        }
    }

    private static string Text(string value) => value ?? "";

    private static int Count<T>(List<T> list) => list?.Count ?? 0;

    private static string Token(TokenRef reference) => reference?.ToToken() ?? "";

    private static IEnumerable<string> Tokens<T>(List<T> refs) where T : TokenRef =>
        (refs ?? new List<T>()).Select(r => r.ToToken()).ToList();
}
