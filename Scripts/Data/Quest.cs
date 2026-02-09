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
