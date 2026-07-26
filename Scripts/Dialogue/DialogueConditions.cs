using System;
using System.Globalization;

// The fixed vocabulary of choice-visibility gates, evaluated by ConditionRef.Id
// against the play context's GameState (dialogue-editor plan Phase 1). These
// relocate the role code's inline gating — a quest-gated choice, an
// already-recruited veto — into named data an editor can pick from. Engine-free
// and total: an unknown verb or a malformed arg is warned and treated as "not
// visible" so a bad edit hides a branch rather than crashing the conversation.
public static class DialogueConditions
{
    public static readonly string[] Ids =
    {
        "quest_state", "has_item", "npc_defeated", "party_has_room",
    };

    // Static check for the editor's pre-save validation (dialogue-editor plan
    // Phase 4): known verb with args that parse? Returns null when valid (a
    // null/empty condition is "always visible", also valid), else a reason.
    public static string Validate(ConditionRef condition)
    {
        if (condition == null || string.IsNullOrEmpty(condition.Id))
        {
            return null;
        }
        var a = condition.Args ?? System.Array.Empty<string>();
        switch (condition.Id)
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
            default:
                return $"unknown condition '{condition.Id}'";
        }
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

        switch (condition.Id)
        {
            case "quest_state":
                return QuestState(condition, context);
            case "has_item":
                return HasItem(condition, context);
            case "npc_defeated":
                return NpcDefeated(condition, context);
            case "party_has_room":
                return !new PartyManager(context.State.Party).IsFull;
            default:
                context.Warn($"Unknown dialogue condition '{condition.Id}'.");
                return false;
        }
    }

    private static bool QuestState(ConditionRef condition, DialogueContext context)
    {
        if (!TryUInt(condition, 0, context, out var questId)
            || !TryEnum<QUESTSUCCESSSTATE>(condition, 1, context, out var expected))
        {
            return false;
        }
        return context.State.GetQuestState(questId) == expected;
    }

    private static bool HasItem(ConditionRef condition, DialogueContext context)
    {
        if (!TryUInt(condition, 0, context, out var itemId))
        {
            return false;
        }
        var quantity = OptionalUInt(condition, 1, 1);
        return context.State.Inventory.CountOf(itemId) >= quantity;
    }

    private static bool NpcDefeated(ConditionRef condition, DialogueContext context)
    {
        var npcId = condition.Arg(0);
        if (string.IsNullOrEmpty(npcId))
        {
            context.Warn("Dialogue condition 'npc_defeated' expected an npc id arg 0.");
            return false;
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
