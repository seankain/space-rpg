using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;

// The serializable dialogue format (dialogue-editor plan Phase 1). Engine-free
// like the rest of Scripts/Data so it round-trips through System.Text.Json and
// the xunit tests compile it without the Godot SDK. A conversation is a flat
// list of nodes joined by string ids rather than a linked object graph — the
// only shape an in-game editor can safely rewire and that JSON serializes
// without cycles-as-references. DialogueRuntime compiles a graph into the
// existing DialogueLine/DialogueChoice tree at play time.
//
// This is the placeholder format until Yarn Spinner lands
// (docs/plans/npc-dialogue-yarn.md); keeping side effects and gating as a small
// named vocabulary (DialogueEffects/DialogueConditions) means a future Yarn
// bridge maps onto the same dispatcher rather than a rewrite.
public class DialogueGraph
{
    // Stable id, matching the file stem: Resources/Dialogue/<Id>.dialogue.json.
    public string Id { get; set; }
    // Node the conversation starts on.
    public string EntryNodeId { get; set; }
    public List<DialogueNode> Nodes { get; set; } = new();

    // A node whose Speaker equals this token renders as the speaking NPC's
    // display name (resolved from the play context), so authored data stays
    // NPC-agnostic. A literal empty Speaker still renders as narration, exactly
    // like DialogueLine.
    public const string SpeakerToken = "$npc";

    public DialogueNode GetNode(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }
        foreach (var node in Nodes)
        {
            if (node.Id == id)
            {
                return node;
            }
        }
        return null;
    }
}

// One screen of dialogue: a speaker + line, an optional on-shown side effect,
// and either an ordered list of choices or a NextNodeId to continue to (choices
// win when both are present, matching DialogueManager). Links are node ids.
public class DialogueNode
{
    public string Id { get; set; }
    public string Speaker { get; set; }
    public string Text { get; set; }
    // Side effect that fires as the line is shown (take an item, complete a
    // quest). Null for a plain line.
    public EffectRef OnShownEffect { get; set; }
    // When set, the line ends in a choice menu instead of a continue prompt.
    public List<DialogueChoiceData> Choices { get; set; }
    // Next node when there are no (visible) choices; null/unknown ends the
    // conversation.
    public string NextNodeId { get; set; }
}

public class DialogueChoiceData
{
    public string Label { get; set; }
    // Side effect fired when the choice is picked, before its target shows.
    public EffectRef Effect { get; set; }
    // Node shown after picking; null/unknown ends the conversation.
    public string NextNodeId { get; set; }
    // Gate: when set, the choice only appears if the condition holds against
    // the current GameState. Null means always visible.
    public ConditionRef Visible { get; set; }
}

// Base of the "named verb + string args" references the data uses instead of
// C# lambdas. Serializes to a compact colon token ("give_item:4:2") so authored
// files stay readable and an editor edits a single field. Id/Args are the shape
// DialogueEffects and DialogueConditions dispatch on.
public abstract class TokenRef
{
    public string Id { get; set; }
    public string[] Args { get; set; } = Array.Empty<string>();

    public string Arg(int index) =>
        Args != null && index >= 0 && index < Args.Length ? Args[index] : null;

    public string ToToken() =>
        Args == null || Args.Length == 0 ? Id : $"{Id}:{string.Join(":", Args)}";

    protected static (string id, string[] args) SplitToken(string token)
    {
        var parts = token.Split(':');
        var args = parts.Length > 1 ? parts[1..] : Array.Empty<string>();
        return (parts[0], args);
    }
}

[JsonConverter(typeof(TokenRefJsonConverter<EffectRef>))]
public sealed class EffectRef : TokenRef
{
    public static EffectRef Parse(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }
        var (id, args) = SplitToken(token);
        return new EffectRef { Id = id, Args = args };
    }
}

[JsonConverter(typeof(TokenRefJsonConverter<ConditionRef>))]
public sealed class ConditionRef : TokenRef
{
    public static ConditionRef Parse(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }
        var (id, args) = SplitToken(token);
        return new ConditionRef { Id = id, Args = args };
    }
}

// Reads/writes EffectRef and ConditionRef as the compact colon string, keeping
// authored .dialogue.json terse instead of a nested { Id, Args } object.
public sealed class TokenRefJsonConverter<T> : JsonConverter<T> where T : TokenRef, new()
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }
        var token = reader.GetString();
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }
        var parts = token.Split(':');
        return new T { Id = parts[0], Args = parts.Length > 1 ? parts[1..] : Array.Empty<string>() };
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }
        writer.WriteStringValue(value.ToToken());
    }
}

// The one place graphs are (de)serialized, so the catalog, the tests, and the
// future editor save path share the same JSON habits as the save system
// (System.Text.Json, indented). Null links/effects are omitted to keep diffs
// small and files reviewable.
public static class DialogueSerialization
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ToJson(DialogueGraph graph) => JsonSerializer.Serialize(graph, Options);

    public static DialogueGraph FromJson(string json) => JsonSerializer.Deserialize<DialogueGraph>(json, Options);
}
