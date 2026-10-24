using System;
using System.Globalization;

// The value-returning half of the condition vocabulary (dialogue-state-conditions
// plan Phase 1). Every DialogueConditions verb is a predicate with its comparison
// baked in — party_size(2) and stat("Charisma", 12) both mean "at least" — which
// leaves state that isn't a yes/no with no way into a conversation at all: a
// script could spend credits and could not ask whether the party had them.
//
// A query reads one number or one string out of the play context's GameState and
// hands it to DialogueConditions to compare against an authored literal. That is
// Yarn Spinner's own model (a side-effect-free function called inside an
// expression) narrowed to a single comparison, which is what keeps a condition a
// flat ConditionRef the in-game editor can render as a form.
//
// Queries are pure reads — upstream's rule for Yarn functions — so evaluating one
// more than once in a conversation is always safe. Engine-free and total like the
// rest of the vocabulary: a malformed argument is warned and reported as "no
// value", which DialogueConditions turns into a hidden branch rather than a crash.
public static class DialogueQueries
{
    public enum ValueKind
    {
        Number,
        Text,
    }

    public static readonly string[] Ids =
    {
        "credits", "party_size", "item_count", "stat",
        "health", "max_health", "level", "quest", "quest_stage", "flag_value",
    };

    // The stats stat(...) can read, spelled as CharacterStats names. Charisma is
    // the one the build plan earmarks for dialogue, but there is no reason to
    // special-case it.
    public static readonly string[] StatNames =
    {
        "Strength", "Intelligence", "Constitution", "Dexterity", "Wisdom", "Charisma",
    };

    public static bool IsQuery(string id) => id != null && Array.IndexOf(Ids, id) >= 0;

    // Which comparisons apply: a number takes all six operators, text only == and
    // != (there is no useful ordering on a quest state or a flag's value).
    public static ValueKind KindOf(string id) =>
        id is "quest" or "flag_value" ? ValueKind.Text : ValueKind.Number;

    // How many arguments the call itself takes, ahead of the comparison's
    // operator and value: credits() none, item_count(3) one.
    public static int Arity(string id) =>
        id is "item_count" or "stat" or "quest" or "quest_stage" or "flag_value" ? 1 : 0;

    // The call's shape, for a validator message.
    public static string Signature(string id) => id switch
    {
        "item_count" => "item_count(<itemId>)",
        "stat" => $"stat(<{string.Join("|", StatNames)}>)",
        "quest" => "quest(<questId>)",
        "quest_stage" => "quest_stage(<questId>)",
        "flag_value" => "flag_value(<flag>)",
        _ => $"{id}()",
    };

    // A whole authored condition using this query, so a validator message shows
    // the comparison rather than only the call.
    public static string Example(string id) => id switch
    {
        "item_count" => "item_count(1) >= 2",
        "stat" => "stat(\"Charisma\") >= 12",
        "quest" => "quest(1) == \"Success\"",
        "quest_stage" => "quest_stage(1) >= 2",
        "flag_value" => "flag_value(\"hale_mood\") == \"angry\"",
        "credits" => "credits() >= 200",
        _ => $"{id}() >= 1",
    };

    // Static check for the editor's pre-save validation and the Yarn compiler:
    // known query, called with arguments that parse against their catalogs?
    // Returns null when valid, else a human-readable reason. The comparison's
    // operator and value are checked by DialogueConditions, which owns them.
    public static string Validate(string id, string[] args)
    {
        if (!IsQuery(id))
        {
            return $"unknown query '{id}'";
        }
        var a = args ?? Array.Empty<string>();
        if (a.Length != Arity(id))
        {
            return $"{id} is written {Signature(id)} compared to a value, e.g. {Example(id)}";
        }
        switch (id)
        {
            case "item_count":
                if (!uint.TryParse(a[0], out var itemId))
                {
                    return $"item_count: item id '{a[0]}' is not a whole number";
                }
                return ItemCatalog.Get(itemId) == null
                    ? $"item_count: no item with id {itemId}"
                    : null;
            case "stat":
                return StatName(a[0]) == null
                    ? $"stat: '{a[0]}' is not a stat ({string.Join(", ", StatNames)})"
                    : null;
            case "quest":
            case "quest_stage":
                if (!uint.TryParse(a[0], out var questId))
                {
                    return $"{id}: quest id '{a[0]}' is not a whole number";
                }
                return QuestCatalog.Get(questId) == null
                    ? $"{id}: no quest with id {questId}"
                    : null;
            case "flag_value":
                if (string.IsNullOrWhiteSpace(a[0]))
                {
                    return "flag_value: the flag name is empty";
                }
                return TokenRef.ArgFormatError("flag_value", "flag name", a[0]);
            default:
                return null;
        }
    }

    // Reads a numeric query. False (with a warning) when the query doesn't
    // return a number, its arguments don't parse, or the state it needs isn't
    // there — the caller hides the branch rather than guessing.
    public static bool TryNumber(string id, string[] args, DialogueContext context, out double value)
    {
        value = 0;
        var state = context?.State;
        if (state == null)
        {
            context?.Warn($"Dialogue query '{id}' ran without game state.");
            return false;
        }
        var a = args ?? Array.Empty<string>();
        switch (id)
        {
            case "credits":
                value = state.Credits;
                return true;
            case "party_size":
                value = state.Party.Count;
                return true;
            case "item_count":
                if (!TryUInt(id, a, 0, context, out var itemId))
                {
                    return false;
                }
                value = state.Inventory.CountOf(itemId);
                return true;
            case "stat":
                return TryStat(a, context, out value);
            case "quest_stage":
                // Which beat of a quest the player is on, 0 for one they
                // haven't started (quest-system.md Phase 1) — so
                // `quest_stage(1) >= 2` is "past the first beat", and reads
                // false for a quest not yet taken rather than needing a
                // quest_state guard beside it.
                if (!TryUInt(id, a, 0, context, out var stageQuestId))
                {
                    return false;
                }
                value = state.GetQuestStage(stageQuestId);
                return true;
            case "health":
            case "max_health":
            case "level":
                var leader = Leader(context);
                if (leader == null)
                {
                    context.Warn($"Dialogue query '{id}' ran with no party leader.");
                    return false;
                }
                value = id switch
                {
                    "health" => leader.HealthPoints,
                    "max_health" => leader.MaxHealthPoints,
                    _ => leader.Level,
                };
                return true;
            default:
                context.Warn($"Dialogue query '{id}' does not return a number.");
                return false;
        }
    }

    // Reads a textual query. quest(1) is the quest state's name ("InProgress"),
    // flag_value("x") the flag's raw value, empty when unset.
    public static bool TryText(string id, string[] args, DialogueContext context, out string value)
    {
        value = null;
        var state = context?.State;
        if (state == null)
        {
            context?.Warn($"Dialogue query '{id}' ran without game state.");
            return false;
        }
        var a = args ?? Array.Empty<string>();
        switch (id)
        {
            case "quest":
                if (!TryUInt(id, a, 0, context, out var questId))
                {
                    return false;
                }
                value = state.GetQuestState(questId).ToString();
                return true;
            case "flag_value":
                var name = a.Length > 0 ? a[0] : null;
                if (string.IsNullOrWhiteSpace(name))
                {
                    context.Warn("Dialogue query 'flag_value' expected a flag name argument.");
                    return false;
                }
                value = state.GetFlag(name) ?? "";
                return true;
            default:
                context.Warn($"Dialogue query '{id}' does not return text.");
                return false;
        }
    }

    // Canonical spelling of an authored stat name, or null when it isn't one.
    // Case-insensitive, so a script can say "charisma".
    public static string StatName(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }
        foreach (var name in StatNames)
        {
            if (name.Equals(raw, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }
        return null;
    }

    // The party leader's stat — the character the player is speaking as, matching
    // who the rest of the leader-facing queries read.
    private static bool TryStat(string[] args, DialogueContext context, out double value)
    {
        value = 0;
        var name = StatName(args.Length > 0 ? args[0] : null);
        if (name == null)
        {
            context.Warn($"Dialogue query 'stat' names unknown stat '{(args.Length > 0 ? args[0] : null)}'.");
            return false;
        }
        var stats = Leader(context)?.Stats;
        if (stats == null)
        {
            context.Warn("Dialogue query 'stat' ran with no party leader.");
            return false;
        }
        value = name switch
        {
            "Strength" => stats.Strength,
            "Intelligence" => stats.Intelligence,
            "Constitution" => stats.Constitution,
            "Dexterity" => stats.Dexterity,
            "Wisdom" => stats.Wisdom,
            _ => stats.Charisma,
        };
        return true;
    }

    private static CharacterEntity Leader(DialogueContext context) =>
        new PartyManager(context.State.Party).Leader;

    private static bool TryUInt(string id, string[] args, int index, DialogueContext context, out uint value)
    {
        var raw = index >= 0 && index < args.Length ? args[index] : null;
        if (uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }
        context.Warn($"Dialogue query '{id}' expected a whole-number argument {index}, got '{raw}'.");
        return false;
    }
}
