using System;
using System.Collections.Generic;

// The one place a quest moves (quest-system.md Phase 2). Storage stays on
// GameState — a save is still just the progress list — but *transitions* happen
// here: prerequisites are checked, the event log is written, and anything
// watching hears about it, however the move was asked for. Before this, a
// dialogue effect logged its own quest lines and the console's identical verbs
// logged nothing, and no prerequisite was read anywhere.
//
// A static class taking the GameState rather than an autoload holding one, like
// QuestMarkerResolver and the console's command classes: the callers all have a
// state in hand already, and this way the whole thing is engine-free and
// testable without booting Godot.
public static class QuestManager
{
    // Every quest move, for views and (Phase 4) reward hooks. Static for the
    // same reason GameEventLog.Recorded and QuestTracking.Changed are: the
    // listeners outlive any one GameState, and loading a save swaps the state
    // underneath them. Subscribers must unsubscribe when they leave the tree or
    // they leak.
    //
    // One event carrying a kind rather than the separate QuestStarted /
    // QuestStageChanged / QuestCompleted the plan sketched: subscribers filter
    // on Kind, and a new kind then costs nobody a second subscription.
    public static event Action<QuestMove> Moved;

    // ----- Starting -----

    // Starts a quest, checking its prerequisites first. Returns null when the
    // quest is now in progress — including when it already was, so a
    // conversation replayed from the top is not an error — else why it can't
    // start, phrased for a console line or a dialogue warning.
    public static string StartQuest(GameState state, uint questId) =>
        StartQuest(state, QuestCatalog.Get(questId));

    // The same for a caller that already holds the definition, and the way a
    // test starts content the catalog doesn't carry (the overload pair
    // QuestMarkerResolver established).
    public static string StartQuest(GameState state, Quest quest)
    {
        if (state == null)
        {
            return "no game in progress";
        }
        if (quest == null)
        {
            return "no such quest";
        }
        if (state.GetQuestState(quest.Id) == QUESTSUCCESSSTATE.InProgress)
        {
            return null;
        }
        if (PrereqProblem(state, quest) is { } problem)
        {
            return problem;
        }
        SetState(state, quest.Id, QUESTSUCCESSSTATE.InProgress);
        return null;
    }

    // Why this quest can't be taken yet, or null when it can. A prerequisite is
    // "quest X must be in state Y"; all of them must hold.
    public static string PrereqProblem(GameState state, uint questId) =>
        PrereqProblem(state, QuestCatalog.Get(questId));

    public static string PrereqProblem(GameState state, Quest quest)
    {
        if (quest == null)
        {
            return "no such quest";
        }
        foreach (var prereq in quest.PrereqQuests ?? new List<QuestPrereqFlag>())
        {
            if (prereq == null)
            {
                continue;
            }
            var actual = state?.GetQuestState(prereq.QuestId) ?? QUESTSUCCESSSTATE.Unstarted;
            if (actual != prereq.SuccessState)
            {
                return $"'{quest.Title}' needs '{Title(prereq.QuestId)}' to be "
                    + $"{prereq.SuccessState}, and it is {actual}";
            }
        }
        return null;
    }

    // ----- State -----

    // Applies a quest state change, logs it, and announces it. False when
    // nothing moved — a quest already in the target state doesn't fill the log
    // with lines for a conversation the player is repeating.
    //
    // The raw transition: prerequisites are StartQuest's business, so this is
    // also the developer console's way past them.
    public static bool SetState(GameState state, uint questId, QUESTSUCCESSSTATE target)
    {
        if (state == null)
        {
            return false;
        }
        var fromState = state.GetQuestState(questId);
        if (fromState == target)
        {
            return false;
        }
        var fromStage = state.GetQuestStage(questId);
        state.SetQuestState(questId, target);
        state.RecordEvent(GameEventKind.Quest, StateLine(questId, target), notify: true);
        Announce(new QuestMove
        {
            Game = state,
            QuestId = questId,
            Kind = KindOf(target),
            FromState = fromState,
            ToState = target,
            FromStage = fromStage,
            ToStage = state.GetQuestStage(questId),
        });
        return true;
    }

    public static bool CompleteQuest(GameState state, uint questId) =>
        SetState(state, questId, QUESTSUCCESSSTATE.Success);

    public static bool FailQuest(GameState state, uint questId) =>
        SetState(state, questId, QUESTSUCCESSSTATE.Failed);

    // One step along Unstarted -> InProgress -> Success, for content that
    // doesn't want to name a target state. Taking the quest this way goes
    // through StartQuest, so `advance_quest` respects prerequisites exactly as
    // `set_quest <id> InProgress` does.
    public static bool AdvanceState(GameState state, uint questId)
    {
        if (state == null)
        {
            return false;
        }
        switch (state.GetQuestState(questId))
        {
            case QUESTSUCCESSSTATE.Unstarted:
                return StartQuest(state, questId) == null
                    && state.GetQuestState(questId) == QUESTSUCCESSSTATE.InProgress;
            case QUESTSUCCESSSTATE.InProgress:
                return CompleteQuest(state, questId);
            default:
                return false;
        }
    }

    // ----- Stages -----

    // Moves a quest to one of its declared stages, logging the new objective
    // and announcing the move. False when the stage doesn't exist or the quest
    // is already on it.
    public static bool SetStage(GameState state, uint questId, uint stageNumber)
    {
        if (state == null)
        {
            return false;
        }
        var fromStage = state.GetQuestStage(questId);
        if (fromStage == stageNumber || !state.SetQuestStage(questId, stageNumber))
        {
            return false;
        }
        var questState = state.GetQuestState(questId);
        if (state.GetCurrentStage(questId) is { } stage)
        {
            state.RecordEvent(GameEventKind.Quest, $"New objective: {stage.SubtitleText}", notify: true);
        }
        Announce(new QuestMove
        {
            Game = state,
            QuestId = questId,
            Kind = QuestMoveKind.StageChanged,
            FromState = questState,
            ToState = questState,
            FromStage = fromStage,
            ToStage = stageNumber,
        });
        return true;
    }

    public static bool AdvanceStage(GameState state, uint questId)
    {
        var next = QuestCatalog.Get(questId)?.NextStageNumber(state?.GetQuestStage(questId) ?? 0) ?? 0;
        return next != 0 && SetStage(state, questId, next);
    }

    // "Get this quest to at least stage n" — the shape a world beat wants
    // (QuestTrigger, a quest pickup). Idempotent by construction: walking back
    // through the same doorway, or reloading a save made after it, moves
    // nothing, so no trigger has to remember that it fired. Only applies to a
    // quest actually in progress.
    public static bool ReachStage(GameState state, uint questId, uint stageNumber)
    {
        if (state == null
            || state.GetQuestState(questId) != QUESTSUCCESSSTATE.InProgress
            || state.GetQuestStage(questId) >= stageNumber)
        {
            return false;
        }
        return SetStage(state, questId, stageNumber);
    }

    private static void Announce(QuestMove move)
    {
        Moved?.Invoke(move);
    }

    private static string StateLine(uint questId, QUESTSUCCESSSTATE target) => target switch
    {
        QUESTSUCCESSSTATE.InProgress => $"Started the quest '{Title(questId)}'.",
        QUESTSUCCESSSTATE.Success => $"Completed the quest '{Title(questId)}'.",
        QUESTSUCCESSSTATE.Failed => $"Failed the quest '{Title(questId)}'.",
        _ => $"Abandoned the quest '{Title(questId)}'.",
    };

    private static QuestMoveKind KindOf(QUESTSUCCESSSTATE target) => target switch
    {
        QUESTSUCCESSSTATE.InProgress => QuestMoveKind.Started,
        QUESTSUCCESSSTATE.Success => QuestMoveKind.Completed,
        QUESTSUCCESSSTATE.Failed => QuestMoveKind.Failed,
        _ => QuestMoveKind.Abandoned,
    };

    // Falls back to the id for a quest the catalog doesn't know, which only a
    // hand-edited save or a test's fabricated definition can produce.
    private static string Title(uint questId) =>
        QuestCatalog.Get(questId)?.Title ?? $"quest {questId}";
}

public enum QuestMoveKind
{
    Started,
    StageChanged,
    Completed,
    Failed,

    // Wound back to Unstarted, which only the developer console does.
    Abandoned,
}

// What happened to a quest, handed to every QuestManager.Moved subscriber. It
// carries the state it happened in, so a subscriber (a reward hook, in Phase 4)
// acts on the same GameState the move was applied to rather than looking one up.
public class QuestMove
{
    public GameState Game { get; set; }

    public uint QuestId { get; set; }

    public QuestMoveKind Kind { get; set; }

    public QUESTSUCCESSSTATE FromState { get; set; }
    public QUESTSUCCESSSTATE ToState { get; set; }

    public uint FromStage { get; set; }
    public uint ToStage { get; set; }

    // The definition, or null for a quest the catalog doesn't carry.
    public Quest Quest => QuestCatalog.Get(QuestId);
}
