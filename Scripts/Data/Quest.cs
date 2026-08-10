using System.Collections.Generic;
using System.Diagnostics.Contracts;

public class Quest
{
    public uint Id {get;set;}
    public string Title{get;set;}
    public string Description{get;set;}
    public bool SideQuest {get;set;}
    public List<QuestPrereqFlag> PrereqQuests {get;set;}
    public QUESTSUCCESSSTATE SuccessState;
    // Where this quest sends the player, in authored order (quest-markers.md
    // Phase 1). Which of them apply at a given moment is decided by each
    // marker's condition, not by this list's order; the map draws the ones the
    // player's tracked quest resolves to.
    public List<QuestMarker> Markers {get;set;} = new();

}

public class QuestStage
{
    public uint StageNumber {get;set;}
    public string SubtitleText {get;set;}
    public string Description {get;set;}

}

public class QuestStagePrereq
{
    
}


public enum QUESTSUCCESSSTATE
{
    Unstarted,
    InProgress,
    Success,
    Failed
}
public class QuestPrereqFlag
{
    public uint QuestId {get;set;}
    public QUESTSUCCESSSTATE SuccessState {get;set;}

}

// Per-save quest progress (definitions stay in QuestCatalog, same
// definition/progress split as items). Lives in GameState.Quests.
public class QuestProgress
{
    public uint QuestId {get;set;}
    public QUESTSUCCESSSTATE State {get;set;} = QUESTSUCCESSSTATE.Unstarted;
    public uint CurrentStageNumber {get;set;}
}
