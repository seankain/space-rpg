using System.Collections.Generic;
using Xunit;

// Dialogue-editor plan Phase 5 engine-free bits: compiling a graph from an
// arbitrary node (the "play from here" preview) and the editor-only layout
// round-trip.
public class DialoguePreviewTests
{
    private static DialogueContext Context(GameState state) => new()
    {
        State = state,
        SpeakerName = "Preview",
    };

    private static DialogueGraph Chain() => new()
    {
        Id = "chain",
        EntryNodeId = "a",
        Nodes = new List<DialogueNode>
        {
            new() { Id = "a", Speaker = DialogueGraph.SpeakerToken, Text = "first", NextNodeId = "b" },
            new() { Id = "b", Speaker = DialogueGraph.SpeakerToken, Text = "second", NextNodeId = "c" },
            new() { Id = "c", Speaker = DialogueGraph.SpeakerToken, Text = "third" },
        },
    };

    [Fact]
    public void CompileFromNodeStartsThere()
    {
        var graph = Chain();
        var line = DialogueRuntime.Compile(graph, Context(new GameState()), "b");

        Assert.Equal("second", line.Text);
        Assert.Equal("third", line.Next.Text);
    }

    [Fact]
    public void CompileFromNullStartUsesEntry()
    {
        var graph = Chain();
        Assert.Equal("first", DialogueRuntime.Compile(graph, Context(new GameState()), null).Text);
        Assert.Equal("first", DialogueRuntime.Compile(graph, Context(new GameState())).Text);
    }

    [Fact]
    public void LayoutRoundTripsThroughJson()
    {
        var layout = new DialogueLayout { Id = "chain" };
        layout.Set("a", 10f, 20f);
        layout.Set("b", 300.5f, -40f);

        var json = DialogueLayoutSerialization.ToJson(layout);
        var restored = DialogueLayoutSerialization.FromJson(json);

        Assert.Equal("chain", restored.Id);
        Assert.Equal(2, restored.Nodes.Count);
        Assert.Equal(300.5f, restored.Get("b").X);
        Assert.Equal(-40f, restored.Get("b").Y);
    }

    [Fact]
    public void LayoutSetUpdatesExistingNode()
    {
        var layout = new DialogueLayout { Id = "x" };
        layout.Set("a", 1f, 2f);
        layout.Set("a", 9f, 8f);

        Assert.Single(layout.Nodes);
        Assert.Equal(9f, layout.Get("a").X);
    }
}
