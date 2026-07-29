using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

// Guards the migrated conversations: every committed file under
// Resources/Dialogue — .dialogue.json or .yarn (npc-dialogue-yarn.md Phase 2
// moved the intro NPCs to Yarn) — must parse, name only known effect/condition
// verbs, and have every link (Next, choice target, router branch) resolve to a
// real node. This is the automated half of the "every branch behaves as before"
// acceptance — it can't play the game, but it catches a typo'd node id or an
// invented verb before a playthrough does.
public class DialogueMigrationTests
{
    private static readonly HashSet<string> KnownEffects = new(DialogueEffects.Ids);
    private static readonly HashSet<string> KnownConditions = new(DialogueConditions.Ids);

    // The intro NPC conversations Phase 2 authored (Rig, Hale, Vex, Marlow x2,
    // Moss), so a dropped file fails loudly instead of silently reverting an
    // NPC to small talk.
    private static readonly string[] ExpectedIds =
    {
        "intro.dockmaster_hale",
        "intro.chief_marlow",
        "intro.chief_marlow_recruit",
        "intro.rig",
        "intro.vex",
        "intro.shopkeeper",
    };

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

    // Both authored formats, loaded the way the game loads them: JSON straight
    // off disk, Yarn through the compiler.
    private static List<(string name, DialogueGraph graph)> LoadAll()
    {
        var dir = DialogueDirectory();
        var loaded = new List<(string, DialogueGraph)>();
        foreach (var path in Directory.GetFiles(dir).OrderBy(p => p, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            if (name.EndsWith(".dialogue.json", StringComparison.Ordinal))
            {
                loaded.Add((name, DialogueSerialization.FromJson(File.ReadAllText(path))));
            }
            else if (name.EndsWith(YarnGraphCompiler.FileSuffix, StringComparison.Ordinal))
            {
                var problems = new List<string>();
                var graph = YarnGraphCompiler.Compile(
                    YarnGraphCompiler.IdFromFileName(name), File.ReadAllText(path), problems);
                Assert.Empty(problems.Select(p => $"{name}: {p}"));
                loaded.Add((name, graph));
            }
        }
        Assert.NotEmpty(loaded);
        return loaded;
    }

    [Fact]
    public void EveryMigratedConversationIsPresent()
    {
        var ids = LoadAll().Select(g => g.graph.Id).ToHashSet();
        foreach (var expected in ExpectedIds)
        {
            Assert.Contains(expected, ids);
        }
    }

    [Fact]
    public void EveryConversationHasValidLinksAndVerbs()
    {
        var problems = new List<string>();
        foreach (var (name, graph) in LoadAll())
        {
            Validate(name, graph, problems);
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    private static void Validate(string name, DialogueGraph graph, List<string> problems)
    {
        void Problem(string message) => problems.Add($"{name}: {message}");

        if (string.IsNullOrEmpty(graph.Id))
        {
            Problem("missing Id");
        }
        var ids = new HashSet<string>();
        foreach (var node in graph.Nodes)
        {
            if (string.IsNullOrEmpty(node.Id))
            {
                Problem("a node has no Id");
            }
            else if (!ids.Add(node.Id))
            {
                Problem($"duplicate node id '{node.Id}'");
            }
        }
        if (string.IsNullOrEmpty(graph.EntryNodeId) || !ids.Contains(graph.EntryNodeId))
        {
            Problem($"entry node '{graph.EntryNodeId}' does not exist");
        }

        void CheckLink(string target, string where)
        {
            if (!string.IsNullOrEmpty(target) && !ids.Contains(target))
            {
                Problem($"{where} points at unknown node '{target}'");
            }
        }
        void CheckEffect(EffectRef effect, string where)
        {
            if (effect != null && !KnownEffects.Contains(effect.Id))
            {
                Problem($"{where} uses unknown effect '{effect.Id}'");
            }
        }
        void CheckCondition(ConditionRef condition, string where)
        {
            if (condition != null && !KnownConditions.Contains(condition.Id))
            {
                Problem($"{where} uses unknown condition '{condition.Id}'");
            }
        }

        foreach (var node in graph.Nodes)
        {
            if (node.Branches is { Count: > 0 })
            {
                CheckLink(node.NextNodeId, $"router '{node.Id}' fallback");
                foreach (var branch in node.Branches)
                {
                    CheckCondition(branch.When, $"router '{node.Id}' branch");
                    CheckLink(branch.ToNodeId, $"router '{node.Id}' branch");
                }
                continue;
            }
            CheckLink(node.NextNodeId, $"node '{node.Id}' Next");
            foreach (var effect in node.OnShownEffects ?? Enumerable.Empty<EffectRef>())
            {
                CheckEffect(effect, $"node '{node.Id}' OnShown");
            }
            foreach (var choice in node.Choices ?? Enumerable.Empty<DialogueChoiceData>())
            {
                CheckLink(choice.NextNodeId, $"node '{node.Id}' choice '{choice.Label}'");
                CheckCondition(choice.Visible, $"node '{node.Id}' choice '{choice.Label}'");
                foreach (var effect in choice.Effects ?? Enumerable.Empty<EffectRef>())
                {
                    CheckEffect(effect, $"node '{node.Id}' choice '{choice.Label}'");
                }
            }
        }
    }

    [Fact]
    public void EveryConversationCompilesWithoutWarnings()
    {
        foreach (var (name, graph) in LoadAll())
        {
            var warnings = new List<string>();
            var context = new DialogueContext
            {
                State = new GameState(),
                SpeakerName = "Test NPC",
                LogWarning = warnings.Add,
            };
            DialogueRuntime.Compile(graph, context);
            Assert.True(warnings.Count == 0, $"{name}: {string.Join("; ", warnings)}");
        }
    }
}
