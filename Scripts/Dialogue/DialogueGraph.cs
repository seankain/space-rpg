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
// This is also the format Yarn compiles into: keeping side effects and gating as
// a small named vocabulary (DialogueEffects/DialogueConditions) is what let the
// Yarn bridge (Scripts/Dialogue/Yarn/, docs/plans/npc-dialogue-yarn.md Phase 1)
// map onto the same dispatcher instead of bringing a second runtime. A
// conversation is authored either as <id>.dialogue.json or as <id>.yarn and ends
// up here either way.
public class DialogueGraph
{
    // Stable id, matching the file stem: Resources/Dialogue/<Id>.dialogue.json
    // (or <Id>.yarn, for a conversation authored in Yarn).
    public string Id { get; set; }
    // Node the conversation starts on.
    public string EntryNodeId { get; set; }
    public List<DialogueNode> Nodes { get; set; } = new();

    // Which on-disk form this conversation was loaded from, so the editor saves
    // it back the same way instead of leaving a .json and a .yarn fighting over
    // one id (npc-dialogue-yarn.md Phase 1). Not part of the file's contents.
    [JsonIgnore]
    public DialogueSourceFormat SourceFormat { get; set; }

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

// How a conversation is written on disk: the editor's own JSON, or Yarn source
// compiled by YarnGraphCompiler. Both produce the same graph — Yarn is an
// authoring format, not a second runtime.
public enum DialogueSourceFormat
{
    Json,
    Yarn,
}

// One screen of dialogue: a speaker + line, on-shown side effects, and either
// an ordered list of choices or a NextNodeId to continue to (choices win when
// both are present, matching DialogueManager). Links are node ids.
//
// A node carrying Branches instead is a *router*: it shows nothing and the
// runtime resolves it to the first branch whose condition holds (see
// DialogueBranch). Chaining single-condition routers is how the migrated
// conversations reproduce the roles' state-based greeting (quest done vs.
// in-progress vs. offer) without embedding code — an editor rewires the
// branches, not a C# switch.
public class DialogueNode
{
    public string Id { get; set; }
    public string Speaker { get; set; }
    public string Text { get; set; }
    // Side effects that fire, in order, as the line is shown (take an item and
    // complete a quest on the same turn-in line). Null/empty for a plain line.
    public List<EffectRef> OnShownEffects { get; set; }
    // When set, the line ends in a choice menu instead of a continue prompt.
    public List<DialogueChoiceData> Choices { get; set; }
    // Next node when there are no (visible) choices; null/unknown ends the
    // conversation. For a router node, the fallback target when no branch
    // matches.
    public string NextNodeId { get; set; }
    // Non-empty makes this a router: an ordered list of conditional gotos
    // evaluated at conversation start (matching when the role code decided the
    // branch). The runtime redirects through it without displaying the node.
    public List<DialogueBranch> Branches { get; set; }
}

public class DialogueChoiceData
{
    public string Label { get; set; }
    // Side effects fired when the choice is picked, in order, before its target
    // shows. Null/empty for a plain navigation choice.
    public List<EffectRef> Effects { get; set; }
    // Node shown after picking; null/unknown ends the conversation.
    public string NextNodeId { get; set; }
    // Gate: when set, the choice only appears if the condition holds against
    // the current GameState. Null means always visible.
    public ConditionRef Visible { get; set; }
}

// One conditional goto in a router node. A null/empty When always matches, so
// it serves as the unconditional fallback and must come last.
public class DialogueBranch
{
    public ConditionRef When { get; set; }
    public string ToNodeId { get; set; }
}

// Base of the "named verb + string args" references the data uses instead of
// C# lambdas. Serializes to a compact colon token ("give_item:4:2") so authored
// files stay readable and an editor edits a single field. Id/Args are the shape
// DialogueEffects and DialogueConditions dispatch on.
public abstract class TokenRef
{
    public const char ArgSeparator = ':';

    public string Id { get; set; }
    public string[] Args { get; set; } = Array.Empty<string>();

    // Verbs whose arguments are free-form text (a world-flag name or value)
    // can't accept the separator: ToToken/Parse would split the argument in
    // two. Returns null when the value is usable, else a reason to report.
    public static string ArgFormatError(string verb, string what, string value) =>
        value != null && value.IndexOf(ArgSeparator) >= 0
            ? $"{verb}: {what} '{value}' can't contain '{ArgSeparator}'"
            : null;

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
