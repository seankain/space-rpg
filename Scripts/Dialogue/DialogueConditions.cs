using System;
using System.Globalization;

// The fixed vocabulary of choice-visibility and branch gates, evaluated by
// ConditionRef.Id against the play context's GameState (dialogue-editor plan
// Phase 1). These relocate the role code's inline gating — a quest-gated choice,
// an already-recruited veto — into named data an editor can pick from.
// Engine-free and total: an unknown verb or a malformed arg is warned and
// treated as "not visible" so a bad edit hides a branch rather than crashing the
// conversation.
//
// A verb is one of two things (dialogue-state-conditions plan Phase 1):
//   * a **predicate** that stands alone — has_item(1), flag("met_hale");
//   * a **query** from DialogueQueries, which returns a value and must be
//     compared to an authored literal — credits() >= 200, quest(1) == "Success".
// A comparison stays a single flat token: the trailing two args are the operator
// and the value (`credits:>=:200`, `stat:Charisma:>:12`). An elided operator
// means ">=" for a number and "==" for text, which is why the tokens written
// before this plan — `party_size:2`, `stat:Charisma:12` — still mean exactly what
// they meant. A predicate may be negated with a '!' on the id (`!has_item:1`),
// which is what finally retires flag("x", "") as the way to spell a negative; a
// comparison negates by inverting its operator instead (see InvertOperator).
public static class DialogueConditions
{
    public static readonly string[] Ids =
    {
        "quest_state", "has_item", "npc_defeated", "party_has_room",
        "flag", "party_size", "stat",
        "credits", "item_count", "health", "max_health", "level", "quest", "quest_stage", "flag_value",
    };

    // Two-character forms come first: the Yarn parser scans this list in order,
    // so ">=" must be found before ">".
    public static readonly string[] Operators = { "==", "!=", ">=", "<=", ">", "<" };

    // Prefixed to an id to invert the condition.
    public const char Negation = '!';

    public static bool IsOperator(string text) =>
        text != null && Array.IndexOf(Operators, text) >= 0;

    // A lone "!" is not a negation of anything — it falls through to "unknown
    // condition" rather than silently inverting a missing verb.
    public static bool IsNegated(string id) =>
        !string.IsNullOrEmpty(id) && id.Length > 1 && id[0] == Negation;

    public static string BareId(string id) => IsNegated(id) ? id[1..] : id;

    // Splits a query condition's args into the call's own arguments, the
    // comparison operator, and the value compared against. False when there is
    // nothing to compare to at all. The operator is optional: without one the
    // last arg is the value and the comparison is the kind's default, which is
    // what makes a pre-plan token like `party_size:2` still read as "at least 2".
    public static bool TrySplitComparison(
        string id, string[] args, out string[] callArgs, out string op, out string value)
    {
        callArgs = Array.Empty<string>();
        op = null;
        value = null;
        var a = args ?? Array.Empty<string>();
        if (a.Length == 0)
        {
            return false;
        }
        if (a.Length >= 2 && IsOperator(a[^2]))
        {
            callArgs = a[..^2];
            op = a[^2];
            value = a[^1];
            return true;
        }
        callArgs = a[..^1];
        op = DefaultOperator(id);
        value = a[^1];
        return true;
    }

    // The operator meaning the opposite of this one. A negated comparison is
    // stored as the inverted operator rather than as a '!' on the id, because
    // Yarn's unary `not` binds tighter than a comparison: a written-out
    // `not credits() >= 200` would read upstream as `(not credits()) >= 200`,
    // which is not what the author meant. `credits() < 200` is unambiguous.
    public static string InvertOperator(string op) => op switch
    {
        "==" => "!=",
        "!=" => "==",
        ">" => "<=",
        "<=" => ">",
        "<" => ">=",
        ">=" => "<",
        _ => null,
    };

    public static string DefaultOperator(string id) =>
        DialogueQueries.KindOf(id) == DialogueQueries.ValueKind.Text ? "==" : ">=";

    // Static check for the editor's pre-save validation (dialogue-editor plan
    // Phase 4): known verb with args that parse? Returns null when valid (a
    // null/empty condition is "always visible", also valid), else a reason.
    public static string Validate(ConditionRef condition)
    {
        if (condition == null || string.IsNullOrEmpty(condition.Id))
        {
            return null;
        }
        var id = BareId(condition.Id);
        var a = condition.Args ?? System.Array.Empty<string>();
        if (DialogueQueries.IsQuery(id))
        {
            return ValidateComparison(id, a);
        }
        switch (id)
        {
            case "quest_state":
                if (a.Length != 2)
                {
                    return "quest_state takes <questId> <state>";
                }
                if (!uint.TryParse(a[0], out var questId))
                {
                    return $"quest_state: quest id '{a[0]}' is not a whole number";
                }
                if (QuestCatalog.Get(questId) == null)
                {
                    return $"quest_state: no quest with id {questId}";
                }
                return IsQuestState(a[1]) ? null : $"quest_state: '{a[1]}' is not a quest state";
            case "has_item":
                if (a.Length is < 1 or > 2)
                {
                    return "has_item takes <itemId> [count]";
                }
                if (!uint.TryParse(a[0], out var itemId))
                {
                    return $"has_item: item id '{a[0]}' is not a whole number";
                }
                if (ItemCatalog.Get(itemId) == null)
                {
                    return $"has_item: no item with id {itemId}";
                }
                if (a.Length == 2 && !uint.TryParse(a[1], out _))
                {
                    return $"has_item: count '{a[1]}' is not a whole number";
                }
                return null;
            case "npc_defeated":
                return a.Length == 1 && !string.IsNullOrEmpty(a[0])
                    ? null
                    : "npc_defeated takes <npcId>";
            case "party_has_room":
                return a.Length == 0 ? null : "party_has_room takes no arguments";
            case "flag":
                if (a.Length is < 1 or > 2)
                {
                    return "flag takes <flag> [value] (one arg asks whether it is set)";
                }
                if (string.IsNullOrWhiteSpace(a[0]))
                {
                    return "flag: the flag name is empty";
                }
                return TokenRef.ArgFormatError("flag", "flag name", a[0])
                    ?? TokenRef.ArgFormatError("flag", "value", a.Length > 1 ? a[1] : null);
            default:
                return $"unknown condition '{id}'";
        }
    }

    // A query's call arguments, then the operator and the literal it is compared
    // against. Text compares only for equality — there is no ordering on a quest
    // state or a flag's value that an author could mean.
    private static string ValidateComparison(string id, string[] args)
    {
        if (!TrySplitComparison(id, args, out var callArgs, out var op, out var value))
        {
            return $"{id} must be compared to a value, e.g. {DialogueQueries.Example(id)}";
        }
        if (DialogueQueries.Validate(id, callArgs) is { } reason)
        {
            return reason;
        }
        if (DialogueQueries.KindOf(id) == DialogueQueries.ValueKind.Text)
        {
            if (op != "==" && op != "!=")
            {
                return $"{id}: text compares with == or != only, got '{op}'";
            }
            return TokenRef.ArgFormatError(id, "value", value);
        }
        return TryNumberLiteral(value, out _)
            ? null
            : $"{id}: '{value}' is not a number";
    }

    private static bool IsQuestState(string raw) =>
        Enum.TryParse<QUESTSUCCESSSTATE>(raw, ignoreCase: true, out var value)
        && Enum.IsDefined(typeof(QUESTSUCCESSSTATE), value);

    public static bool Evaluate(ConditionRef condition, DialogueContext context)
    {
        // No gate means always visible.
        if (condition == null || string.IsNullOrEmpty(condition.Id))
        {
            return true;
        }
        if (context?.State == null)
        {
            context?.Warn($"Dialogue condition '{condition.Id}' evaluated without game state; hiding.");
            return false;
        }

        var result = EvaluateCore(BareId(condition.Id), condition, context);
        // A condition that could not be evaluated hides its branch whether or not
        // it was negated: a bad edit must only ever close a path, never open one.
        if (result == null)
        {
            return false;
        }
        return IsNegated(condition.Id) ? !result.Value : result.Value;
    }

    // True/false, or null for "couldn't tell" — an unknown verb, an argument that
    // doesn't parse, or state that isn't there.
    private static bool? EvaluateCore(string id, ConditionRef condition, DialogueContext context)
    {
        if (DialogueQueries.IsQuery(id))
        {
            return Compare(id, condition, context);
        }
        switch (id)
        {
            case "quest_state":
                return QuestState(condition, context);
            case "has_item":
                return HasItem(condition, context);
            case "npc_defeated":
                return NpcDefeated(condition, context);
            case "party_has_room":
                return !new PartyManager(context.State.Party).IsFull;
            case "flag":
                return Flag(condition, context);
            default:
                context.Warn($"Unknown dialogue condition '{id}'.");
                return null;
        }
    }

    // credits() >= 200: read the value out of GameState now, compare it to the
    // authored literal. The read happens here rather than being cached, which is
    // why a query has to stay a pure read.
    private static bool? Compare(string id, ConditionRef condition, DialogueContext context)
    {
        if (!TrySplitComparison(id, condition.Args, out var callArgs, out var op, out var value))
        {
            context.Warn($"Dialogue condition '{id}' has no value to compare against.");
            return null;
        }
        if (DialogueQueries.KindOf(id) == DialogueQueries.ValueKind.Text)
        {
            if (!DialogueQueries.TryText(id, callArgs, context, out var text))
            {
                return null;
            }
            var equal = string.Equals(text ?? "", value ?? "", StringComparison.OrdinalIgnoreCase);
            if (op == "==")
            {
                return equal;
            }
            if (op == "!=")
            {
                return !equal;
            }
            context.Warn($"Dialogue condition '{id}' compares text, which supports == and != only (got '{op}').");
            return null;
        }
        if (!TryNumberLiteral(value, out var right))
        {
            context.Warn($"Dialogue condition '{id}' expected a number to compare against, got '{value}'.");
            return null;
        }
        if (!DialogueQueries.TryNumber(id, callArgs, context, out var left))
        {
            return null;
        }
        switch (op)
        {
            case "==": return left == right;
            case "!=": return left != right;
            case ">": return left > right;
            case "<": return left < right;
            case ">=": return left >= right;
            case "<=": return left <= right;
            default:
                context.Warn($"Dialogue condition '{id}' names unknown operator '{op}'.");
                return null;
        }
    }

    private static bool TryNumberLiteral(string raw, out double value) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    // flag("met_hale") — is it set at all? flag("hale_mood", "angry") — does it
    // hold that value? A second arg of "" asks whether the flag is unset, which
    // predates negation and still works; new content should write
    // not flag("met_hale") instead.
    private static bool? Flag(ConditionRef condition, DialogueContext context)
    {
        var name = condition.Arg(0);
        if (string.IsNullOrWhiteSpace(name))
        {
            context.Warn("Dialogue condition 'flag' expected a flag name arg 0.");
            return null;
        }
        if (condition.Args.Length < 2)
        {
            return context.State.IsFlagSet(name);
        }
        var expected = condition.Arg(1) ?? "";
        var actual = context.State.GetFlag(name) ?? "";
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool? QuestState(ConditionRef condition, DialogueContext context)
    {
        if (!TryUInt(condition, 0, context, out var questId)
            || !TryEnum<QUESTSUCCESSSTATE>(condition, 1, context, out var expected))
        {
            return null;
        }
        return context.State.GetQuestState(questId) == expected;
    }

    private static bool? HasItem(ConditionRef condition, DialogueContext context)
    {
        if (!TryUInt(condition, 0, context, out var itemId))
        {
            return null;
        }
        var quantity = OptionalUInt(condition, 1, 1);
        return context.State.Inventory.CountOf(itemId) >= quantity;
    }

    private static bool? NpcDefeated(ConditionRef condition, DialogueContext context)
    {
        var npcId = condition.Arg(0);
        if (string.IsNullOrEmpty(npcId))
        {
            context.Warn("Dialogue condition 'npc_defeated' expected an npc id arg 0.");
            return null;
        }
        return context.State.IsNpcDefeated(npcId);
    }

    private static bool TryUInt(ConditionRef condition, int index, DialogueContext context, out uint value)
    {
        var raw = condition.Arg(index);
        if (uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }
        context.Warn($"Dialogue condition '{condition.Id}' expected a whole-number arg {index}, got '{raw}'.");
        return false;
    }

    private static bool TryEnum<TEnum>(ConditionRef condition, int index, DialogueContext context, out TEnum value)
        where TEnum : struct
    {
        var raw = condition.Arg(index);
        if (Enum.TryParse(raw, ignoreCase: true, out value) && Enum.IsDefined(typeof(TEnum), value))
        {
            return true;
        }
        context.Warn($"Dialogue condition '{condition.Id}' expected a {typeof(TEnum).Name} arg {index}, got '{raw}'.");
        return false;
    }

    private static uint OptionalUInt(ConditionRef condition, int index, uint fallback) =>
        uint.TryParse(condition.Arg(index), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
}
