using System;
using System.Globalization;

// The fixed vocabulary of side effects a conversation can name, dispatched by
// EffectRef.Id (dialogue-editor plan Phase 1). Each case is a relocated role
// lambda body: give/take an item, move a quest, adjust credits, or hand a
// scene action (battle, shop, recruit) to the context's host. Engine-free and
// crash-free — an unknown verb or a malformed arg is warned and skipped, never
// thrown, so a bad edit can't take down a live conversation (the editor's
// pre-save validator, Phase 4, is what catches these at author time).
public static class DialogueEffects
{
    // The recognized verbs, so the editor and validator can offer/check them
    // without reaching into the switch.
    public static readonly string[] Ids =
    {
        "give_item", "take_item", "set_quest", "advance_quest",
        "credits", "recruit", "start_battle", "open_shop",
    };

    public static void Run(EffectRef effect, DialogueContext context)
    {
        if (effect == null || string.IsNullOrEmpty(effect.Id))
        {
            return;
        }
        if (context?.State == null)
        {
            context?.Warn($"Dialogue effect '{effect.Id}' ran without game state; skipping.");
            return;
        }

        switch (effect.Id)
        {
            case "give_item":
                GiveItem(effect, context);
                break;
            case "take_item":
                TakeItem(effect, context);
                break;
            case "set_quest":
                SetQuest(effect, context);
                break;
            case "advance_quest":
                AdvanceQuest(effect, context);
                break;
            case "credits":
                Credits(effect, context);
                break;
            case "recruit":
                Recruit(effect, context);
                break;
            case "start_battle":
                // start_battle:despawn removes the loser on the player's win
                // (a lone challenger); plain start_battle leaves them standing
                // (a quest-giver whose demand ends in a fight).
                RequireHost(context, effect)?.StartBattle(effect.Arg(0) == "despawn");
                break;
            case "open_shop":
                RequireHost(context, effect)?.OpenShop();
                break;
            default:
                context.Warn($"Unknown dialogue effect '{effect.Id}'.");
                break;
        }
    }

    private static void GiveItem(EffectRef effect, DialogueContext context)
    {
        if (!TryUInt(effect, 0, context, out var itemId))
        {
            return;
        }
        var quantity = OptionalUInt(effect, 1, 1);
        if (ItemCatalog.Get(itemId) == null)
        {
            context.Warn($"give_item names unknown item id {itemId}.");
            return;
        }
        context.State.Inventory.Add(itemId, quantity);
    }

    private static void TakeItem(EffectRef effect, DialogueContext context)
    {
        if (!TryUInt(effect, 0, context, out var itemId))
        {
            return;
        }
        var quantity = OptionalUInt(effect, 1, 1);
        if (!context.State.Inventory.Remove(itemId, quantity))
        {
            context.Warn($"take_item could not remove {quantity}x item {itemId} (not enough held).");
        }
    }

    private static void SetQuest(EffectRef effect, DialogueContext context)
    {
        if (!TryUInt(effect, 0, context, out var questId)
            || !TryEnum<QUESTSUCCESSSTATE>(effect, 1, context, out var target))
        {
            return;
        }
        context.State.SetQuestState(questId, target);
    }

    // Bumps a quest one step along Unstarted -> InProgress -> Success, clamped
    // at the ends; a generic "move it forward" verb for authors who don't want
    // to name an explicit target state.
    private static void AdvanceQuest(EffectRef effect, DialogueContext context)
    {
        if (!TryUInt(effect, 0, context, out var questId))
        {
            return;
        }
        var next = context.State.GetQuestState(questId) switch
        {
            QUESTSUCCESSSTATE.Unstarted => QUESTSUCCESSSTATE.InProgress,
            QUESTSUCCESSSTATE.InProgress => QUESTSUCCESSSTATE.Success,
            var current => current,
        };
        context.State.SetQuestState(questId, next);
    }

    // credits:<amount> adds (or, with a negative amount, spends) party credits,
    // clamped at zero so it can never underflow the unsigned balance.
    private static void Credits(EffectRef effect, DialogueContext context)
    {
        if (!TryInt(effect, 0, context, out var delta))
        {
            return;
        }
        var balance = (long)context.State.Credits + delta;
        context.State.Credits = (uint)Math.Max(0, balance);
    }

    private static void Recruit(EffectRef effect, DialogueContext context)
    {
        if (!TryULong(effect, 0, context, out var partyCharacterId))
        {
            return;
        }
        RequireHost(context, effect)?.Recruit(partyCharacterId);
    }

    private static IDialogueEffectHost RequireHost(DialogueContext context, EffectRef effect)
    {
        if (context.Host == null)
        {
            context.Warn($"Dialogue effect '{effect.Id}' needs a scene host, but none was supplied.");
            return null;
        }
        return context.Host;
    }

    private static bool TryUInt(EffectRef effect, int index, DialogueContext context, out uint value)
    {
        var raw = effect.Arg(index);
        if (uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }
        context.Warn($"Dialogue effect '{effect.Id}' expected a whole-number arg {index}, got '{raw}'.");
        return false;
    }

    private static bool TryULong(EffectRef effect, int index, DialogueContext context, out ulong value)
    {
        var raw = effect.Arg(index);
        if (ulong.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }
        context.Warn($"Dialogue effect '{effect.Id}' expected a whole-number arg {index}, got '{raw}'.");
        return false;
    }

    private static bool TryInt(EffectRef effect, int index, DialogueContext context, out int value)
    {
        var raw = effect.Arg(index);
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }
        context.Warn($"Dialogue effect '{effect.Id}' expected a whole-number arg {index}, got '{raw}'.");
        return false;
    }

    private static bool TryEnum<TEnum>(EffectRef effect, int index, DialogueContext context, out TEnum value)
        where TEnum : struct
    {
        var raw = effect.Arg(index);
        if (Enum.TryParse(raw, ignoreCase: true, out value) && Enum.IsDefined(typeof(TEnum), value))
        {
            return true;
        }
        context.Warn($"Dialogue effect '{effect.Id}' expected a {typeof(TEnum).Name} arg {index}, got '{raw}'.");
        return false;
    }

    private static uint OptionalUInt(EffectRef effect, int index, uint fallback) =>
        uint.TryParse(effect.Arg(index), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
}
