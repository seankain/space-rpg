using System;

// Stages that reach themselves (quest-system.md Phase 4). A beat that *is* a
// state — "you have the cube", "Vex is down" — should be true whenever it is
// asked, not the moment something remembered to say so: the scripted hooks
// (a conversation's `set_stage`, a QuestTrigger) only fire while the player is
// standing in them, so picking the cube up before taking the quest used to
// leave the journal a beat behind forever.
//
// So a stage may carry a ReachedWhen condition, and this re-checks them when
// something happened that could have made one true. Not per-frame polling and
// not a bespoke signal per kind of state: every gain, purchase, battle and
// conversation the player has already records a GameEventLog entry, which is
// exactly the set of moments worth re-asking after.
//
// Engine-free like the rest of the transition layer; Install is what ties the
// static log hook to whichever GameState is running (SaveManager owns that, and
// hands it over as a lookup rather than an instance, because loading a save
// swaps it).
public static class QuestObjectiveWatcher
{
    private static Func<GameState> currentState;
    private static bool installed;

    // Re-entrancy guard: reaching a stage records its own log entry, which
    // arrives back here through the same hook.
    private static bool evaluating;

    // Subscribes to the event log for the lifetime of the process. Idempotent —
    // a second call replaces the lookup rather than adding a second listener,
    // so a reloaded scene tree can't double-subscribe.
    public static void Install(Func<GameState> state)
    {
        currentState = state;
        if (installed)
        {
            return;
        }
        installed = true;
        GameEventLog.Recorded += _ => Evaluate(currentState?.Invoke());
    }

    // Advances every quest in progress as far as its stage conditions allow,
    // and returns how many stages moved. Safe to call at any time: it only ever
    // moves forward, and a stage with no condition stops the walk — a beat
    // something has to announce can't be skipped past by the one after it
    // happening to be true.
    public static int Evaluate(GameState state, Action<string> warn = null)
    {
        if (state == null || evaluating)
        {
            return 0;
        }
        evaluating = true;
        try
        {
            var moved = 0;
            foreach (var progress in state.Quests.ToArray())
            {
                if (progress?.State == QUESTSUCCESSSTATE.InProgress)
                {
                    moved += Advance(state, QuestCatalog.Get(progress.QuestId), warn);
                }
            }
            return moved;
        }
        finally
        {
            evaluating = false;
        }
    }

    // The same for one quest a caller already holds — and the way a test walks
    // content the catalog doesn't carry, the overload pair QuestMarkerResolver
    // established.
    public static int Evaluate(GameState state, Quest quest, Action<string> warn = null)
    {
        if (state == null || evaluating
            || quest == null
            || state.GetQuestState(quest.Id) != QUESTSUCCESSSTATE.InProgress)
        {
            return 0;
        }
        evaluating = true;
        try
        {
            return Advance(state, quest, warn);
        }
        finally
        {
            evaluating = false;
        }
    }

    private static int Advance(GameState state, Quest quest, Action<string> warn)
    {
        if (quest == null || !quest.HasStages)
        {
            return 0;
        }
        var context = new DialogueContext
        {
            State = state,
            LogWarning = warn ?? (_ => { }),
        };
        var moved = 0;
        // One stage at a time, so a quest whose next two beats both hold —
        // loading a save made after both happened — walks through them in
        // order and logs each objective rather than jumping to the last.
        while (true)
        {
            var next = quest.NextStageNumber(state.GetQuestStage(quest.Id));
            if (next == 0)
            {
                break;
            }
            // A stage with no condition is one something has to announce; the
            // walk stops there rather than stepping over it.
            var stage = quest.GetStage(next);
            if (stage?.ReachedWhen == null || !DialogueConditions.Evaluate(stage.ReachedWhen, context))
            {
                break;
            }
            if (!QuestManager.ReachStage(state, quest.Id, next))
            {
                break;
            }
            moved++;
        }
        return moved;
    }
}
