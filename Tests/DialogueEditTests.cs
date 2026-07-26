using System.Collections.Generic;
using System.Linq;
using Xunit;

// Dialogue-editor plan Phase 4 acceptance: graph edits keep a valid graph, the
// validator flags the blocking problems (dangling link, missing entry, bad
// effect arg) and warns on orphans, and an edited graph round-trips byte-stably
// so a save produces a clean PR diff.
public class DialogueEditTests
{
    private static DialogueGraph TwoNodeGraph()
    {
        return new DialogueGraph
        {
            Id = "test",
            EntryNodeId = "a",
            Nodes = new List<DialogueNode>
            {
                new()
                {
                    Id = "a",
                    Speaker = DialogueGraph.SpeakerToken,
                    Text = "hi",
                    Choices = new List<DialogueChoiceData>
                    {
                        new() { Label = "go", NextNodeId = "b" },
                    },
                },
                new() { Id = "b", Speaker = DialogueGraph.SpeakerToken, Text = "bye" },
            },
        };
    }

    private static bool Valid(DialogueGraph graph) =>
        !DialogueValidation.HasErrors(DialogueValidation.Validate(graph));

    // --- Editing keeps a valid graph -----------------------------------------

    [Fact]
    public void AddNodeYieldsUniqueIdAndStaysValid()
    {
        var graph = TwoNodeGraph();
        var first = DialogueGraphEditing.AddNode(graph, router: false);
        var second = DialogueGraphEditing.AddNode(graph, router: false);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(4, graph.Nodes.Count);
        // New nodes are unreachable (warnings) but not errors.
        Assert.True(Valid(graph));
    }

    [Fact]
    public void DeleteNodeClearsReferencesSoNoDanglingLink()
    {
        var graph = TwoNodeGraph();
        Assert.True(DialogueGraphEditing.DeleteNode(graph, "b"));

        Assert.Null(graph.GetNode("b"));
        // The choice that pointed at "b" now ends the conversation, not dangles.
        Assert.Null(graph.GetNode("a").Choices[0].NextNodeId);
        Assert.True(Valid(graph));
    }

    [Fact]
    public void RenameNodeRepointsEntryAndLinks()
    {
        var graph = TwoNodeGraph();
        Assert.True(DialogueGraphEditing.RenameNode(graph, "a", "start"));

        Assert.Equal("start", graph.EntryNodeId);
        Assert.NotNull(graph.GetNode("start"));
        Assert.True(Valid(graph));

        // Renaming a link target repoints the referrers.
        Assert.True(DialogueGraphEditing.RenameNode(graph, "b", "farewell"));
        Assert.Equal("farewell", graph.GetNode("start").Choices[0].NextNodeId);
        Assert.True(Valid(graph));
    }

    [Fact]
    public void RenameNodeRejectsCollision()
    {
        var graph = TwoNodeGraph();
        Assert.False(DialogueGraphEditing.RenameNode(graph, "a", "b"));
        Assert.NotNull(graph.GetNode("a"));
    }

    [Fact]
    public void MoveReordersChoices()
    {
        var choices = new List<DialogueChoiceData>
        {
            new() { Label = "first" },
            new() { Label = "second" },
        };
        DialogueGraphEditing.Move(choices, 0, 1);
        Assert.Equal("second", choices[0].Label);
        Assert.Equal("first", choices[1].Label);
        // Clamped at the ends — no throw, no change.
        DialogueGraphEditing.Move(choices, 1, 1);
        Assert.Equal("first", choices[1].Label);
    }

    // --- Validation ----------------------------------------------------------

    [Fact]
    public void DanglingLinkIsAnError()
    {
        var graph = TwoNodeGraph();
        graph.GetNode("a").Choices[0].NextNodeId = "nowhere";
        var issues = DialogueValidation.Validate(graph);

        Assert.True(DialogueValidation.HasErrors(issues));
        Assert.Contains(issues, i => i.Message.Contains("nowhere"));
    }

    [Fact]
    public void MissingEntryIsAnError()
    {
        var graph = TwoNodeGraph();
        graph.EntryNodeId = "ghost";
        Assert.True(DialogueValidation.HasErrors(DialogueValidation.Validate(graph)));
    }

    [Fact]
    public void BadEffectArgIsAnError()
    {
        var graph = TwoNodeGraph();
        graph.GetNode("b").OnShownEffects = new List<EffectRef> { EffectRef.Parse("set_quest:1:Nope") };
        var issues = DialogueValidation.Validate(graph);

        Assert.True(DialogueValidation.HasErrors(issues));
        Assert.Contains(issues, i => i.Message.Contains("quest state"));
    }

    [Fact]
    public void OrphanNodeIsAWarningNotAnError()
    {
        var graph = TwoNodeGraph();
        graph.Nodes.Add(new DialogueNode { Id = "island", Speaker = DialogueGraph.SpeakerToken, Text = "alone" });
        var issues = DialogueValidation.Validate(graph);

        Assert.False(DialogueValidation.HasErrors(issues));
        Assert.Contains(issues, i => i.Severity == DialogueSeverity.Warning && i.Message.Contains("island"));
    }

    [Fact]
    public void EffectAndConditionValidateAcceptGoodAndRejectBad()
    {
        Assert.Null(DialogueEffects.Validate(EffectRef.Parse($"give_item:{ItemCatalog.MedkitId}:2")));
        Assert.Null(DialogueEffects.Validate(EffectRef.Parse("start_battle:despawn")));
        Assert.NotNull(DialogueEffects.Validate(EffectRef.Parse("give_item:9999")));
        Assert.NotNull(DialogueEffects.Validate(EffectRef.Parse("teleport")));

        Assert.Null(DialogueConditions.Validate(ConditionRef.Parse("quest_state:1:Success")));
        Assert.Null(DialogueConditions.Validate(ConditionRef.Parse("party_has_room")));
        Assert.NotNull(DialogueConditions.Validate(ConditionRef.Parse("has_item:9999")));
        Assert.NotNull(DialogueConditions.Validate(ConditionRef.Parse("party_has_room:1")));
    }

    // --- Save round-trip -----------------------------------------------------

    [Fact]
    public void EditedGraphRoundTripsByteStably()
    {
        var graph = TwoNodeGraph();
        DialogueGraphEditing.AddNode(graph, router: true);
        graph.GetNode("b").OnShownEffects = new List<EffectRef> { EffectRef.Parse("set_quest:1:Success") };

        var json = DialogueSerialization.ToJson(graph);
        var reloaded = DialogueSerialization.FromJson(json);
        var again = DialogueSerialization.ToJson(reloaded);

        // Serialize -> load -> serialize is a fixed point, so a save produces a
        // clean, minimal diff.
        Assert.Equal(json, again);
    }

    [Fact]
    public void CloneIsIndependentOfSource()
    {
        var graph = TwoNodeGraph();
        var clone = DialogueGraphEditing.Clone(graph);
        clone.GetNode("a").Text = "changed";

        Assert.Equal("hi", graph.GetNode("a").Text);
        Assert.Equal("changed", clone.GetNode("a").Text);
    }
}
