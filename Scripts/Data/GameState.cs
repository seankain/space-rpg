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
    public List<CharacterEntity> Party{get;set;} = new();
    // Shared party inventory; equipment stays per-character on EquipSlots.
    public Inventory Inventory {get;set;} = new();
    // Per-quest progress against QuestCatalog definitions; quests the player
    // hasn't touched have no entry (implicitly Unstarted).
    public List<QuestProgress> Quests {get;set;} = new();

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
}
