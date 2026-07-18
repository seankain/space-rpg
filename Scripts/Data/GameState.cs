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

    public QUESTSUCCESSSTATE GetQuestState(uint questId) =>
        Quests.FirstOrDefault(q => q.QuestId == questId)?.State ?? QUESTSUCCESSSTATE.Unstarted;

    public void SetQuestState(uint questId, QUESTSUCCESSSTATE state)
    {
        var progress = Quests.FirstOrDefault(q => q.QuestId == questId);
        if (progress == null)
        {
            progress = new QuestProgress { QuestId = questId };
            Quests.Add(progress);
        }
        progress.State = state;
    }

    public bool IsNpcDefeated(string npcId) => DefeatedNpcs.Contains(npcId);

    public void MarkNpcDefeated(string npcId)
    {
        if (!DefeatedNpcs.Contains(npcId))
        {
            DefeatedNpcs.Add(npcId);
        }
    }
}
