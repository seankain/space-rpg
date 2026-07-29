using System.Collections.Generic;
using System.Text.Json;

// Editor-only node positions for the graph-canvas view (dialogue-editor plan
// Phase 5), stored in a sibling Resources/Dialogue/<id>.layout.json so the
// gameplay <id>.dialogue.json stays minimal and the game never loads layout
// (DialogueCatalog only scans *.dialogue.json and *.yarn). Engine-free and JSON-
// serializable like the graph itself, so the round-trip is unit-tested.
public class DialogueLayout
{
    public string Id { get; set; }
    public List<DialogueNodePosition> Nodes { get; set; } = new();

    public DialogueNodePosition Get(string nodeId)
    {
        foreach (var position in Nodes)
        {
            if (position.NodeId == nodeId)
            {
                return position;
            }
        }
        return null;
    }

    public void Set(string nodeId, float x, float y)
    {
        var existing = Get(nodeId);
        if (existing != null)
        {
            existing.X = x;
            existing.Y = y;
            return;
        }
        Nodes.Add(new DialogueNodePosition { NodeId = nodeId, X = x, Y = y });
    }
}

public class DialogueNodePosition
{
    public string NodeId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
}

public static class DialogueLayoutSerialization
{
    // Same habits as DialogueSerialization: indented, null-skipping, for clean
    // committed diffs.
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ToJson(DialogueLayout layout) => JsonSerializer.Serialize(layout, Options);

    public static DialogueLayout FromJson(string json) => JsonSerializer.Deserialize<DialogueLayout>(json, Options);
}
