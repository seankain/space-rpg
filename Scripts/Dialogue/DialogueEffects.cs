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
        "credits", "recruit", "start_battle", "open_shop", "set_flag",
        "play_anim",
    };

    // Static check for the editor's pre-save validation (dialogue-editor plan
    // Phase 4): does this effect name a known verb with args that parse against
    // its vocabulary? Returns null when valid, else a human-readable reason.
    // Engine-free like Run, so DialogueValidation and its tests use it without
    // Godot.
    public static string Validate(EffectRef effect)
    {
        if (effect == null || string.IsNullOrEmpty(effect.Id))
        {
            return "effect has no id";
        }
        var a = effect.Args ?? System.Array.Empty<string>();
        switch (effect.Id)
        {
            case "give_item":
            case "take_item":
                if (a.Length is < 1 or > 2)
                {
                    return $"{effect.Id} takes <itemId> [qty]";
                }
                if (!uint.TryParse(a[0], out var itemId))
                {
                    return $"{effect.Id}: item id '{a[0]}' is not a whole number";
                }
                if (ItemCatalog.Get(itemId) == null)
                {
                    return $"{effect.Id}: no item with id {itemId}";
                }
                if (a.Length == 2 && !uint.TryParse(a[1], out _))
                {
                    return $"{effect.Id}: quantity '{a[1]}' is not a whole number";
                }
                return null;
            case "set_quest":
                if (a.Length != 2)
                {
                    return "set_quest takes <questId> <state>";
                }
                if (!uint.TryParse(a[0], out var setQuestId))
                {
                    return $"set_quest: quest id '{a[0]}' is not a whole number";
                }
                if (QuestCatalog.Get(setQuestId) == null)
                {
                    return $"set_quest: no quest with id {setQuestId}";
                }
                return IsQuestState(a[1]) ? null : $"set_quest: '{a[1]}' is not a quest state";
            case "advance_quest":
                if (a.Length != 1)
                {
                    return "advance_quest takes <questId>";
                }
                if (!uint.TryParse(a[0], out var advanceQuestId))
                {
                    return $"advance_quest: quest id '{a[0]}' is not a whole number";
                }
                return QuestCatalog.Get(advanceQuestId) == null
                    ? $"advance_quest: no quest with id {advanceQuestId}"
                    : null;
            case "credits":
                if (a.Length != 1)
                {
                    return "credits takes <amount>";
                }
                return int.TryParse(a[0], out _) ? null : $"credits: amount '{a[0]}' is not a number";
            case "recruit":
                if (a.Length != 1)
                {
                    return "recruit takes <partyCharacterId>";
                }
                return ulong.TryParse(a[0], out _) ? null : $"recruit: id '{a[0]}' is not a whole number";
            case "start_battle":
                if (a.Length == 0 || (a.Length == 1 && a[0] == "despawn"))
                {
                    return null;
                }
                return "start_battle takes an optional 'despawn'";
            case "open_shop":
                return a.Length == 0 ? null : "open_shop takes no arguments";
            case "set_flag":
                if (a.Length is < 1 or > 2)
                {
                    return "set_flag takes <flag> [value] (default 'true'; an empty value clears it)";
                }
                if (string.IsNullOrWhiteSpace(a[0]))
                {
                    return "set_flag: the flag name is empty";
                }
                return TokenRef.ArgFormatError("set_flag", "flag name", a[0])
                    ?? TokenRef.ArgFormatError("set_flag", "value", a.Length > 1 ? a[1] : null);
            case "play_anim":
                if (a.Length is < 1 or > 2)
                {
                    return "play_anim takes <clip> [loop]";
                }
                if (DialogueAnimations.Resolve(a[0]) == null)
                {
                    return $"play_anim: '{a[0]}' is not a dialogue clip "
                        + $"({string.Join(", ", DialogueAnimations.Ids)})";
                }
                return a.Length == 1 || a[1] == LoopArg
                    ? null
                    : $"play_anim: second argument is '{LoopArg}' or nothing, got '{a[1]}'";
            default:
                return $"unknown effect '{effect.Id}'";
        }
    }

    private static bool IsQuestState(string raw) =>
        Enum.TryParse<QUESTSUCCESSSTATE>(raw, ignoreCase: true, out var value)
        && Enum.IsDefined(typeof(QUESTSUCCESSSTATE), value);

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
            case "set_flag":
                SetFlag(effect, context);
                break;
            case "play_anim":
                PlayAnimation(effect, context);
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

    // The optional second argument of play_anim: keep the clip looping instead
    // of playing it once and settling back into idle.
    public const string LoopArg = "loop";

    // play_anim:<clip>[:loop] plays a gesture on the speaking NPC — the one
    // verb here that changes nothing but what you see. A one-shot returns the
    // NPC to idle when it finishes; a looping one runs until the conversation
    // closes (or another play_anim replaces it).
    private static void PlayAnimation(EffectRef effect, DialogueContext context)
    {
        var clip = DialogueAnimations.Resolve(effect.Arg(0));
        if (clip == null)
        {
            context.Warn($"Dialogue effect 'play_anim' names unknown clip '{effect.Arg(0)}'.");
            return;
        }
        RequireHost(context, effect)?.PlayAnimation(clip, effect.Arg(1) == LoopArg);
    }

    // set_flag:<name>[:<value>] records ad-hoc world state ("met_hale") that
    // survives saves. The value defaults to "true" — the common case is a
    // yes/no — and an explicitly empty one clears the flag.
    private static void SetFlag(EffectRef effect, DialogueContext context)
    {
        var name = effect.Arg(0);
        if (string.IsNullOrWhiteSpace(name))
        {
            context.Warn("Dialogue effect 'set_flag' expected a flag name arg 0.");
            return;
        }
        context.State.SetFlag(name, effect.Args.Length > 1 ? effect.Arg(1) : "true");
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
