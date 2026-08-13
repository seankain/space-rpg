using System.Collections.Generic;
using System.Linq;

public class Quest
{
    public uint Id {get;set;}
    public string Title{get;set;}
    public string Description{get;set;}
    public bool SideQuest {get;set;}
    public List<QuestPrereqFlag> PrereqQuests {get;set;}
    // The NpcDefinition.NpcId of whoever offers this quest ("intro.vex"), or
    // empty for one nobody hands out (quest-system.md Phase 3). This is the
    // quest naming its giver, which is the direction the journal and the
    // on-NPC indicator need; NpcRole.RequiredQuestId points the other way and
    // answers a different question.
    //
    // Authored, not derived: a conversation could in principle be scanned for
    // a `set_quest` verb, but a quest is offered by whoever the writer says
    // offers it, and dialogue moves between NPCs freely.
    public string GiverNpcId {get;set;} = "";
    // Where this quest sends the player, in authored order (quest-markers.md
    // Phase 1). Which of them apply at a given moment is decided by each
    // marker's condition, not by this list's order; the map draws the ones the
    // player's tracked quest resolves to.
    public List<QuestMarker> Markers {get;set;} = new();
    // The quest's beats in order, numbered from 1 (quest-system.md Phase 1).
    // A quest may declare none, which is what every quest did before stages
    // existed: it then runs on its success state and its markers alone.
    //
    // A stage moves one of three ways: something says so (a dialogue
    // `set_stage`, the console's `quest stage`), the player walks into the
    // place it stands for (`QuestTrigger`), or it reaches itself because its
    // own ReachedWhen condition holds.
    public List<QuestStage> Stages {get;set;} = new();
    // What finishing this quest hands the party (quest-system.md Phase 4), or
    // null for a quest that pays in story alone. Granted by QuestManager the
    // moment the quest succeeds, so it doesn't matter which conversation,
    // trigger or console verb finished it.
    public QuestReward Reward {get;set;}

    public bool HasStages => Stages is { Count: > 0 };

    // The number a quest starts on, or 0 for a quest with no stages. Always 1
    // for a valid definition (QuestCatalog rejects any other first number);
    // read off the list rather than assumed, so the two can't drift.
    public uint FirstStageNumber => HasStages ? Stages[0].StageNumber : 0;

    // The stage a progress number names, or null when the quest has no such
    // stage. Stage 0 is "no stage": a quest that hasn't started, one that
    // declares no stages, or one taken in a save written before stages existed.
    public QuestStage GetStage(uint stageNumber) =>
        stageNumber == 0 ? null : Stages?.FirstOrDefault(stage => stage.StageNumber == stageNumber);

    // The stage after this one in authored order, or 0 when there is none. From
    // 0 that is the first stage, which is how a quest carried by an older save
    // rejoins the ladder the first time something advances it.
    public uint NextStageNumber(uint stageNumber)
    {
        if (!HasStages)
        {
            return 0;
        }
        if (stageNumber == 0)
        {
            return FirstStageNumber;
        }
        var index = Stages.FindIndex(stage => stage.StageNumber == stageNumber);
        return index >= 0 && index + 1 < Stages.Count ? Stages[index + 1].StageNumber : 0;
    }
}

// One beat of a quest: what the player is doing right now. The subtitle is the
// short line the journal and HUD show under the quest title ("Find the Maguffin
// Cube"); the description is the longer journal paragraph, and may be empty.
public class QuestStage
{
    public uint StageNumber {get;set;}
    public string SubtitleText {get;set;}
    public string Description {get;set;}

    // When this stage reaches itself, from the same condition vocabulary a
    // marker's VisibleWhen uses (quest-system.md Phase 4). Null for a beat
    // something has to announce — a conversation's `set_stage`, a QuestTrigger
    // — which is every beat that is an event rather than a state.
    //
    // A condition beats a script wherever the beat *is* a state ("you have the
    // cube", "Vex is down"): it is true whenever it is evaluated, so it can't
    // be missed by doing things in an order nobody anticipated. The two can
    // share a stage — a condition reaches it, and an alternate route may still
    // set it explicitly.
    public ConditionRef ReachedWhen {get;set;}

    public static QuestStage Create(
        uint stageNumber, string subtitleText, string description = "", string reachedWhen = null) => new()
    {
        StageNumber = stageNumber,
        SubtitleText = subtitleText,
        Description = description,
        ReachedWhen = ConditionRef.Parse(reachedWhen),
    };
}

// What a quest pays out. Every part is optional; a quest may reward nothing at
// all, which is what every quest did before this existed — the keycard for
// "Clear the Deck" was handed over by a give_item in the turn-in dialogue,
// where a second way to finish the quest would have missed it.
public class QuestReward
{
    public uint Credits {get;set;}

    // Split across nobody: experience is granted to every party member, the
    // way a battle's is.
    public uint ExperiencePoints {get;set;}

    public List<ItemStack> Items {get;set;} = new();

    public bool IsEmpty =>
        Credits == 0 && ExperiencePoints == 0 && (Items == null || Items.Count == 0);

    public static ItemStack Item(uint itemId, uint quantity = 1) =>
        new() { ItemId = itemId, Quantity = quantity };
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
    // Which of the definition's stages the player is on; 0 means none, either
    // because the quest hasn't started, because it declares no stages, or
    // because the save predates stages. Moved only through GameState's stage
    // methods, which bound it against the definition.
    public uint CurrentStageNumber {get;set;}
}
