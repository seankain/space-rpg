using System.Collections.Generic;
using Xunit;

// Dialogue-editor plan Phase 1 acceptance: the serializable graph round-trips
// through JSON, compiles into the existing DialogueLine runtime tree with links
// resolved and choices filtered by condition, dispatches effects to GameState,
// and reports a dangling link instead of crashing.
public class DialogueGraphTests
{
    // Records the scene-facing verbs so the pure test build can assert battle/
    // shop/recruit were triggered without a live Npc.
    private sealed class FakeHost : IDialogueEffectHost
    {
        public int Battles;
        public int Shops;
        public readonly List<ulong> Recruited = new();

        public bool LastBattleDespawned;

        public void StartBattle(bool despawnOnDefeat)
        {
            Battles++;
            LastBattleDespawned = despawnOnDefeat;
        }

        public void OpenShop() => Shops++;
        public void Recruit(ulong partyCharacterId) => Recruited.Add(partyCharacterId);

        public readonly List<string> Animations = new();

        public void PlayAnimation(string clip, bool loop) =>
            Animations.Add(loop ? $"{clip}:loop" : clip);
    }

    private static DialogueContext Context(
        GameState state, out List<string> warnings, IDialogueEffectHost host = null, string speaker = "Hale")
    {
        var captured = new List<string>();
        warnings = captured;
        return new DialogueContext
        {
            State = state,
            SpeakerName = speaker,
            Host = host,
            LogWarning = captured.Add,
        };
    }

    private static DialogueNode Line(string id, string text, string next = null) => new()
    {
        Id = id,
        Speaker = DialogueGraph.SpeakerToken,
        Text = text,
        NextNodeId = next,
    };

    // --- Serialization -------------------------------------------------------

    [Fact]
    public void GraphRoundTripsThroughJson()
    {
        var graph = new DialogueGraph
        {
            Id = "hale.maguffin",
            EntryNodeId = "offer",
            Nodes = new List<DialogueNode>
            {
                new()
                {
                    Id = "offer",
                    Speaker = DialogueGraph.SpeakerToken,
                    Text = "Track down my cube?",
                    Choices = new List<DialogueChoiceData>
                    {
                        new()
                        {
                            Label = "I'll find it",
                            Effects = new List<EffectRef> { EffectRef.Parse("set_quest:1:InProgress") },
                            NextNodeId = "thanks",
                        },
                        new()
                        {
                            Label = "Turn it in",
                            Effects = new List<EffectRef> { EffectRef.Parse("take_item:1") },
                            Visible = ConditionRef.Parse("has_item:1"),
                            NextNodeId = "done",
                        },
                    },
                },
                new() { Id = "thanks", Speaker = DialogueGraph.SpeakerToken, Text = "Much obliged." },
                new()
                {
                    Id = "done",
                    Speaker = DialogueGraph.SpeakerToken,
                    Text = "You have my thanks.",
                    OnShownEffects = new List<EffectRef>
                    {
                        EffectRef.Parse("take_item:1"),
                        EffectRef.Parse("set_quest:1:Success"),
                    },
                },
            },
        };

        var json = DialogueSerialization.ToJson(graph);
        // Effects/conditions serialize as the compact colon token (in a JSON
        // array for the effect lists), not a nested object, so files stay terse.
        Assert.Contains("set_quest:1:Success", json);
        Assert.Contains("has_item:1", json);

        var restored = DialogueSerialization.FromJson(json);
        Assert.Equal("hale.maguffin", restored.Id);
        Assert.Equal("offer", restored.EntryNodeId);
        Assert.Equal(3, restored.Nodes.Count);

        var offer = restored.GetNode("offer");
        Assert.Equal(2, offer.Choices.Count);
        Assert.Equal("set_quest", offer.Choices[0].Effects[0].Id);
        Assert.Equal(new[] { "1", "InProgress" }, offer.Choices[0].Effects[0].Args);
        Assert.Equal("has_item", offer.Choices[1].Visible.Id);
        var done = restored.GetNode("done");
        Assert.Equal(2, done.OnShownEffects.Count);
        Assert.Equal("take_item:1", done.OnShownEffects[0].ToToken());
        Assert.Equal("set_quest:1:Success", done.OnShownEffects[1].ToToken());
    }

    // --- Link resolution -----------------------------------------------------

    [Fact]
    public void CompileResolvesNodeIdsToNextLinks()
    {
        var graph = new DialogueGraph
        {
            Id = "chain",
            EntryNodeId = "a",
            Nodes = new List<DialogueNode> { Line("a", "first", "b"), Line("b", "second", "c"), Line("c", "third") },
        };

        var root = DialogueRuntime.Compile(graph, Context(new GameState(), out _));

        Assert.Equal("first", root.Text);
        Assert.Equal("second", root.Next.Text);
        Assert.Equal("third", root.Next.Next.Text);
        Assert.Null(root.Next.Next.Next);
    }

    [Fact]
    public void CompilePreservesChoiceOrder()
    {
        var graph = new DialogueGraph
        {
            Id = "menu",
            EntryNodeId = "root",
            Nodes = new List<DialogueNode>
            {
                new()
                {
                    Id = "root",
                    Speaker = DialogueGraph.SpeakerToken,
                    Text = "Pick one",
                    Choices = new List<DialogueChoiceData>
                    {
                        new() { Label = "First" },
                        new() { Label = "Second" },
                        new() { Label = "Third" },
                    },
                },
            },
        };

        var root = DialogueRuntime.Compile(graph, Context(new GameState(), out _));

        Assert.Collection(root.Choices,
            c => Assert.Equal("First", c.Label),
            c => Assert.Equal("Second", c.Label),
            c => Assert.Equal("Third", c.Label));
    }

    [Fact]
    public void CompileReusesSharedNodesAndSurvivesCycles()
    {
        var graph = new DialogueGraph
        {
            Id = "loop",
            EntryNodeId = "a",
            Nodes = new List<DialogueNode> { Line("a", "here", "b"), Line("b", "there", "a") },
        };

        var root = DialogueRuntime.Compile(graph, Context(new GameState(), out _));

        // The back-edge resolves to the same instance rather than recursing
        // forever.
        Assert.Same(root, root.Next.Next);
    }

    [Fact]
    public void SpeakerTokenBecomesContextName()
    {
        var graph = new DialogueGraph
        {
            Id = "s",
            EntryNodeId = "a",
            Nodes = new List<DialogueNode>
            {
                new() { Id = "a", Speaker = DialogueGraph.SpeakerToken, Text = "hi", NextNodeId = "b" },
                new() { Id = "b", Speaker = "Station AI", Text = "narration-ish", NextNodeId = "c" },
                new() { Id = "c", Speaker = null, Text = "unattributed" },
            },
        };

        var root = DialogueRuntime.Compile(graph, Context(new GameState(), out _, speaker: "Dockmaster Hale"));

        Assert.Equal("Dockmaster Hale", root.Speaker);
        Assert.Equal("Station AI", root.Next.Speaker);
        Assert.Null(root.Next.Next.Speaker);
    }

    // --- Condition filtering -------------------------------------------------

    [Fact]
    public void ConditionHidesAndShowsGatedChoice()
    {
        var graph = new DialogueGraph
        {
            Id = "gate",
            EntryNodeId = "root",
            Nodes = new List<DialogueNode>
            {
                new()
                {
                    Id = "root",
                    Speaker = DialogueGraph.SpeakerToken,
                    Text = "Well?",
                    Choices = new List<DialogueChoiceData>
                    {
                        new() { Label = "Always here" },
                        new() { Label = "Turn in cube", Visible = ConditionRef.Parse("has_item:1") },
                    },
                },
            },
        };

        var without = DialogueRuntime.Compile(graph, Context(new GameState(), out _));
        Assert.Single(without.Choices);
        Assert.Equal("Always here", without.Choices[0].Label);

        var state = new GameState();
        state.Inventory.Add(ItemCatalog.MaguffinCubeId);
        var with = DialogueRuntime.Compile(graph, Context(state, out _));
        Assert.Equal(2, with.Choices.Count);
        Assert.Equal("Turn in cube", with.Choices[1].Label);
    }

    [Fact]
    public void NodeWithAllChoicesHiddenFollowsNextLink()
    {
        var graph = new DialogueGraph
        {
            Id = "fallthrough",
            EntryNodeId = "root",
            Nodes = new List<DialogueNode>
            {
                new()
                {
                    Id = "root",
                    Speaker = DialogueGraph.SpeakerToken,
                    Text = "hmm",
                    NextNodeId = "after",
                    Choices = new List<DialogueChoiceData>
                    {
                        new() { Label = "Gated", Visible = ConditionRef.Parse("has_item:1") },
                    },
                },
                Line("after", "moving on"),
            },
        };

        var root = DialogueRuntime.Compile(graph, Context(new GameState(), out _));

        Assert.Null(root.Choices);
        Assert.Equal("moving on", root.Next.Text);
    }

    // --- Effect dispatch -----------------------------------------------------

    [Fact]
    public void OnShownEffectMutatesGameState()
    {
        var state = new GameState();
        var graph = new DialogueGraph
        {
            Id = "e",
            EntryNodeId = "a",
            Nodes = new List<DialogueNode>
            {
                new()
                {
                    Id = "a",
                    Speaker = DialogueGraph.SpeakerToken,
                    Text = "Done.",
                    OnShownEffects = new List<EffectRef> { EffectRef.Parse("set_quest:1:Success") },
                },
            },
        };

        var root = DialogueRuntime.Compile(graph, Context(state, out _));
        Assert.Equal(QUESTSUCCESSSTATE.Unstarted, state.GetQuestState(1));

        // Effects fire on play, not on compile.
        root.OnShown();
        Assert.Equal(QUESTSUCCESSSTATE.Success, state.GetQuestState(1));
    }

    [Fact]
    public void ChoiceEffectRunsGiveAndTakeItem()
    {
        var state = new GameState();
        state.Inventory.Add(ItemCatalog.MaguffinCubeId);

        var give = EffectRef.Parse($"give_item:{ItemCatalog.MedkitId}:2");
        var take = EffectRef.Parse($"take_item:{ItemCatalog.MaguffinCubeId}");
        var context = Context(state, out _);

        DialogueEffects.Run(give, context);
        DialogueEffects.Run(take, context);

        Assert.Equal(2u, state.Inventory.CountOf(ItemCatalog.MedkitId));
        Assert.Equal(0u, state.Inventory.CountOf(ItemCatalog.MaguffinCubeId));
    }

    [Fact]
    public void AdvanceQuestStepsForwardAndClamps()
    {
        var state = new GameState();
        var context = Context(state, out _);
        var advance = EffectRef.Parse("advance_quest:1");

        DialogueEffects.Run(advance, context);
        Assert.Equal(QUESTSUCCESSSTATE.InProgress, state.GetQuestState(1));
        DialogueEffects.Run(advance, context);
        Assert.Equal(QUESTSUCCESSSTATE.Success, state.GetQuestState(1));
        DialogueEffects.Run(advance, context);
        Assert.Equal(QUESTSUCCESSSTATE.Success, state.GetQuestState(1));
    }

    [Fact]
    public void CreditsEffectAddsAndClampsAtZero()
    {
        var state = new GameState { Credits = 100 };
        var context = Context(state, out _);

        DialogueEffects.Run(EffectRef.Parse("credits:50"), context);
        Assert.Equal(150u, state.Credits);

        DialogueEffects.Run(EffectRef.Parse("credits:-500"), context);
        Assert.Equal(0u, state.Credits);
    }

    [Fact]
    public void SceneEffectsDelegateToHost()
    {
        var host = new FakeHost();
        var context = Context(new GameState(), out _, host);

        DialogueEffects.Run(EffectRef.Parse("start_battle:despawn"), context);
        DialogueEffects.Run(EffectRef.Parse("open_shop"), context);
        DialogueEffects.Run(EffectRef.Parse("recruit:2"), context);

        Assert.Equal(1, host.Battles);
        Assert.True(host.LastBattleDespawned);
        Assert.Equal(1, host.Shops);
        Assert.Equal(new List<ulong> { 2ul }, host.Recruited);

        // Plain start_battle leaves the loser standing.
        DialogueEffects.Run(EffectRef.Parse("start_battle"), context);
        Assert.False(host.LastBattleDespawned);
    }

    // --- Robustness ----------------------------------------------------------

    [Fact]
    public void DanglingNextLinkIsReportedNotCrashed()
    {
        var graph = new DialogueGraph
        {
            Id = "broken",
            EntryNodeId = "a",
            Nodes = new List<DialogueNode> { Line("a", "off the edge", "nowhere") },
        };

        var context = Context(new GameState(), out var warnings);
        var root = DialogueRuntime.Compile(graph, context);

        Assert.Equal("off the edge", root.Text);
        Assert.Null(root.Next);
        Assert.Contains(warnings, w => w.Contains("nowhere"));
    }

    [Fact]
    public void UnknownEffectAndBadArgAreWarnedNotThrown()
    {
        var context = Context(new GameState(), out var warnings);

        DialogueEffects.Run(EffectRef.Parse("teleport_to_moon"), context);
        DialogueEffects.Run(EffectRef.Parse("give_item:not_a_number"), context);

        Assert.Contains(warnings, w => w.Contains("teleport_to_moon"));
        Assert.Contains(warnings, w => w.Contains("give_item"));
    }

    [Fact]
    public void SceneEffectWithoutHostWarnsInsteadOfThrowing()
    {
        var context = Context(new GameState(), out var warnings);

        DialogueEffects.Run(EffectRef.Parse("start_battle"), context);

        Assert.Contains(warnings, w => w.Contains("start_battle"));
    }

    // --- Routers (Phase 2 state-based branching) -----------------------------

    // A chain of single-condition routers, the shape every migrated greeting
    // uses: quest done -> done line; in progress + holding item -> turn-in;
    // in progress -> reminder; otherwise -> offer.
    private static DialogueGraph GreetingGraph() => new()
    {
        Id = "greet",
        EntryNodeId = "entry",
        Nodes = new List<DialogueNode>
        {
            new()
            {
                Id = "entry",
                Branches = new List<DialogueBranch>
                {
                    new() { When = ConditionRef.Parse("quest_state:1:Success"), ToNodeId = "done" },
                    new() { When = ConditionRef.Parse("quest_state:1:InProgress"), ToNodeId = "inprogress" },
                    new() { ToNodeId = "offer" },
                },
            },
            new()
            {
                Id = "inprogress",
                Branches = new List<DialogueBranch>
                {
                    new() { When = ConditionRef.Parse("has_item:1"), ToNodeId = "turnin" },
                    new() { ToNodeId = "reminder" },
                },
            },
            Line("done", "All done."),
            Line("turnin", "Hand it over."),
            Line("reminder", "Any luck?"),
            Line("offer", "Care to help?"),
        },
    };

    [Fact]
    public void RouterPicksFirstMatchingBranch()
    {
        var graph = GreetingGraph();

        var offer = DialogueRuntime.Compile(graph, Context(new GameState(), out _));
        Assert.Equal("Care to help?", offer.Text);

        var inProgress = new GameState();
        inProgress.SetQuestState(1, QUESTSUCCESSSTATE.InProgress);
        Assert.Equal("Any luck?", DialogueRuntime.Compile(graph, Context(inProgress, out _)).Text);

        var holding = new GameState();
        holding.SetQuestState(1, QUESTSUCCESSSTATE.InProgress);
        holding.Inventory.Add(ItemCatalog.MaguffinCubeId);
        Assert.Equal("Hand it over.", DialogueRuntime.Compile(graph, Context(holding, out _)).Text);

        var done = new GameState();
        done.SetQuestState(1, QUESTSUCCESSSTATE.Success);
        Assert.Equal("All done.", DialogueRuntime.Compile(graph, Context(done, out _)).Text);
    }

    [Fact]
    public void RouterCycleIsReportedNotHung()
    {
        var graph = new DialogueGraph
        {
            Id = "spin",
            EntryNodeId = "a",
            Nodes = new List<DialogueNode>
            {
                new() { Id = "a", Branches = new List<DialogueBranch> { new() { ToNodeId = "b" } } },
                new() { Id = "b", Branches = new List<DialogueBranch> { new() { ToNodeId = "a" } } },
            },
        };

        var context = Context(new GameState(), out var warnings);
        var root = DialogueRuntime.Compile(graph, context);

        Assert.Null(root);
        Assert.Contains(warnings, w => w.Contains("cycle"));
    }

    [Fact]
    public void OnShownRunsEveryEffectInOrder()
    {
        var state = new GameState();
        state.Inventory.Add(ItemCatalog.MaguffinCubeId);
        var graph = new DialogueGraph
        {
            Id = "turnin",
            EntryNodeId = "complete",
            Nodes = new List<DialogueNode>
            {
                new()
                {
                    Id = "complete",
                    Speaker = DialogueGraph.SpeakerToken,
                    Text = "Thanks.",
                    OnShownEffects = new List<EffectRef>
                    {
                        EffectRef.Parse($"take_item:{ItemCatalog.MaguffinCubeId}"),
                        EffectRef.Parse("set_quest:1:Success"),
                    },
                },
            },
        };

        var root = DialogueRuntime.Compile(graph, Context(state, out _));
        root.OnShown();

        Assert.Equal(0u, state.Inventory.CountOf(ItemCatalog.MaguffinCubeId));
        Assert.Equal(QUESTSUCCESSSTATE.Success, state.GetQuestState(1));
    }
}
