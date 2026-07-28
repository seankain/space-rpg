using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

public class GameState
{
    public string CurrentLevelPath {get;set;}
    public string LocationName {get;set;}
    public Guid LocationId {get;set;}
    // Null until first captured; a fresh game spawns at the level's Spawn marker instead.
    public Vector3? PlayerPosition {get;set;}
    public Vector3? PlayerRotation {get;set;}
    // Where the exit door of the current interior (shop, house) returns the
    // player: captured by the entrance Door, consumed by the exit Door. All
    // null while the player is outside.
    public string ReturnLevelPath {get;set;}
    public string ReturnLocationName {get;set;}
    public Vector3? ReturnPosition {get;set;}
    public Vector3? ReturnRotation {get;set;}
    // Party-shared credits for trading with merchants. The initializer doubles
    // as the migration default: pre-v5 saves have no Credits field, so they
    // load with the new-game amount.
    public uint Credits {get;set;} = StartingCredits;
    public const uint StartingCredits = 250;
    public List<CharacterEntity> Party{get;set;} = new();
    // Shared party inventory; equipment stays per-character on EquipSlots.
    public Inventory Inventory {get;set;} = new();
    // Per-quest progress against QuestCatalog definitions; quests the player
    // hasn't touched have no entry (implicitly Unstarted).
    public List<QuestProgress> Quests {get;set;} = new();
    // Stable NpcIds (NpcDefinition.NpcId, e.g. "intro.vex") of battle NPCs
    // the party has beaten, so defeated challengers stay down across saves
    // and chunk reloads and quests can check for them. Pre-v7 saves stored
    // display names; SaveManager migrates those through NpcDatabase on load.
    public List<string> DefeatedNpcs {get;set;} = new();
    // Ad-hoc world flags a conversation sets and reads — "met_hale",
    // "hale_mood" — the general-purpose slot the quest/inventory/defeated-NPC
    // state doesn't cover (npc-dialogue-yarn.md Phase 3). Values are strings so
    // a flag can carry a little state ("angry", "2") as well as a yes/no.
    // Pre-v8 saves have no Flags and load with none.
    //
    // Keys are normalized (trimmed, lower-cased) by the accessors below rather
    // than by a case-insensitive comparer, because System.Text.Json rebuilds
    // this dictionary with the default comparer on load — a comparer set here
    // would silently apply to new games only.
    public Dictionary<string, string> Flags {get;set;} = new();

    public string GetFlag(string name)
    {
        var key = FlagKey(name);
        return key != null && Flags.TryGetValue(key, out var value) ? value : null;
    }

    // What flag("x") asks: the flag holds something that isn't an explicit no.
    public bool IsFlagSet(string name) => IsFlagTruthy(GetFlag(name));

    // Setting an empty value clears the flag, so "unset" has one representation
    // and a cleared flag doesn't linger in every save file.
    public void SetFlag(string name, string value)
    {
        var key = FlagKey(name);
        if (key == null)
        {
            return;
        }
        if (string.IsNullOrEmpty(value))
        {
            Flags.Remove(key);
            return;
        }
        Flags[key] = value;
    }

    public void ClearFlag(string name) => SetFlag(name, null);

    // A value counts as set unless it explicitly says otherwise, so
    // `set_flag met_hale false` reads as "not met" rather than "met, with a
    // funny value".
    public static bool IsFlagTruthy(string value) =>
        !string.IsNullOrEmpty(value)
        && !value.Equals("false", StringComparison.OrdinalIgnoreCase)
        && value != "0";

    private static string FlagKey(string name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToLowerInvariant();
    }

    public QUESTSUCCESSSTATE GetQuestState(uint questId) =>
        Quests.FirstOrDefault(q => q.QuestId == questId)?.State ?? QUESTSUCCESSSTATE.Unstarted;

    // Get-or-create the progress record for a quest, so state changes and stage
    // edits (the in-game editor's quest commands) share one creation path.
    public QuestProgress GetOrAddQuestProgress(uint questId)
    {
        var progress = Quests.FirstOrDefault(q => q.QuestId == questId);
        if (progress == null)
        {
            progress = new QuestProgress { QuestId = questId };
            Quests.Add(progress);
        }
        return progress;
    }

    public void SetQuestState(uint questId, QUESTSUCCESSSTATE state) =>
        GetOrAddQuestProgress(questId).State = state;

    public bool IsNpcDefeated(string npcId) => DefeatedNpcs.Contains(npcId);

    public void MarkNpcDefeated(string npcId)
    {
        if (!DefeatedNpcs.Contains(npcId))
        {
            DefeatedNpcs.Add(npcId);
        }
    }
}
