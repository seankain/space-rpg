using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

// quest-system.md Phase 2: the one place a quest moves. Storage stays on
// GameState, but starting, advancing, completing and staging go through
// QuestManager — which is what finally reads a prerequisite, and what makes a
// console move log the same line a conversation's move does.
public class QuestManagerTests
{
    private const uint FetchQuest = QuestCatalog.ReturnTheMaguffinId;
    private const uint BountyQuest = QuestCatalog.ClearTheDeckId;

    private static Quest Gated(uint id, params QuestPrereqFlag[] prereqs) => new()
    {
        Id = id,
        Title = $"Quest {id}",
        PrereqQuests = prereqs.ToList(),
    };

    private static QuestPrereqFlag Needs(uint questId, QUESTSUCCESSSTATE state) =>
        new() { QuestId = questId, SuccessState = state };

    // Records the moves an action makes to one game, then unsubscribes — a
    // leaked static subscription would follow the whole test run. Moved is
    // static and xunit runs test classes in parallel, so the listener keeps
    // only the moves belonging to this test's own GameState.
    private static List<QuestMove> Watch(GameState state, Action act)
    {
        var moves = new List<QuestMove>();
        void Listener(QuestMove move)
        {
            if (ReferenceEquals(move.Game, state))
            {
                moves.Add(move);
            }
        }
        QuestManager.Moved += Listener;
        try
        {
            act();
        }
        finally
        {
            QuestManager.Moved -= Listener;
        }
        return moves;
    }

    // --- Prerequisites -------------------------------------------------------

    [Fact]
    public void AQuestWithNoPrerequisitesIsAlwaysAvailable()
    {
        // Both shipped quests are this, which is why nothing noticed for so
        // long that prerequisites were never read.
        Assert.Null(QuestManager.PrereqProblem(new GameState(), FetchQuest));
        Assert.Null(QuestManager.PrereqProblem(new GameState(), BountyQuest));
    }

    [Fact]
    public void APrerequisiteHasToBeInTheStateItNames()
    {
        var state = new GameState();
        var chained = Gated(101, Needs(FetchQuest, QUESTSUCCESSSTATE.Success));

        var problem = QuestManager.PrereqProblem(state, chained);
        Assert.Contains("Return the Maguffin", problem);
        Assert.Contains("Success", problem);
        Assert.Contains("Unstarted", problem);

        // In progress is not done.
        state.SetQuestState(FetchQuest, QUESTSUCCESSSTATE.InProgress);
        Assert.NotNull(QuestManager.PrereqProblem(state, chained));

        state.SetQuestState(FetchQuest, QUESTSUCCESSSTATE.Success);
        Assert.Null(QuestManager.PrereqProblem(state, chained));
    }

    [Fact]
    public void EveryPrerequisiteHasToHold()
    {
        var state = new GameState();
        state.SetQuestState(FetchQuest, QUESTSUCCESSSTATE.Success);
        var chained = Gated(101,
            Needs(FetchQuest, QUESTSUCCESSSTATE.Success),
            Needs(BountyQuest, QUESTSUCCESSSTATE.Success));

        Assert.Contains("Clear the Deck", QuestManager.PrereqProblem(state, chained));

        state.SetQuestState(BountyQuest, QUESTSUCCESSSTATE.Success);
        Assert.Null(QuestManager.PrereqProblem(state, chained));
    }

    [Fact]
    public void StartingIsRefusedUntilThePrerequisitesAreMet()
    {
        var state = new GameState();
        var chained = Gated(101, Needs(FetchQuest, QUESTSUCCESSSTATE.Success));

        Assert.NotNull(QuestManager.StartQuest(state, chained));
        Assert.Equal(QUESTSUCCESSSTATE.Unstarted, state.GetQuestState(101));
        Assert.Empty(state.EventLog.Entries);

        state.SetQuestState(FetchQuest, QUESTSUCCESSSTATE.Success);

        Assert.Null(QuestManager.StartQuest(state, chained));
        Assert.Equal(QUESTSUCCESSSTATE.InProgress, state.GetQuestState(101));
    }

    [Fact]
    public void StartingAQuestAlreadyInProgressIsNotAComplaint()
    {
        // A conversation replayed from the top says set_quest again; that is
        // not a content bug and must not read as one.
        var state = new GameState();
        Assert.Null(QuestManager.StartQuest(state, FetchQuest));
        var entries = state.EventLog.Entries.Count;

        Assert.Null(QuestManager.StartQuest(state, FetchQuest));

        Assert.Equal(entries, state.EventLog.Entries.Count);
    }

    [Fact]
    public void StartingAnUnknownQuestIsRefused()
    {
        var state = new GameState();
        Assert.NotNull(QuestManager.StartQuest(state, 99999));
        Assert.Empty(state.Quests);
    }

    // --- Logging, wherever the move came from --------------------------------

    [Theory]
    [InlineData(QUESTSUCCESSSTATE.InProgress, "Started the quest")]
    [InlineData(QUESTSUCCESSSTATE.Success, "Completed the quest")]
    [InlineData(QUESTSUCCESSSTATE.Failed, "Failed the quest")]
    public void AStateMoveIsLoggedAndToasted(QUESTSUCCESSSTATE target, string expected)
    {
        var state = new GameState();

        Assert.True(QuestManager.SetState(state, FetchQuest, target));

        var entry = state.EventLog.Entries.Last();
        Assert.Equal(GameEventKind.Quest, entry.Kind);
        Assert.Contains(expected, entry.Text);
        Assert.Contains(QuestCatalog.Get(FetchQuest).Title, entry.Text);
        Assert.True(entry.Notify);
    }

    [Fact]
    public void AMoveThatChangesNothingIsNotLogged()
    {
        var state = new GameState();
        QuestManager.SetState(state, FetchQuest, QUESTSUCCESSSTATE.InProgress);
        var entries = state.EventLog.Entries.Count;

        Assert.False(QuestManager.SetState(state, FetchQuest, QUESTSUCCESSSTATE.InProgress));

        Assert.Equal(entries, state.EventLog.Entries.Count);
    }

    [Fact]
    public void ConsoleMovesAreLoggedTheSameWayDialogueMovesAre()
    {
        // The reason transitions were pulled into one place: `quest set` used
        // to change the game silently while <<set_quest>> wrote a line.
        var state = new GameState();

        EditorQuestCommands.Run(state, new[] { "set", FetchQuest.ToString(), "success" });

        Assert.Contains(state.EventLog.Entries,
            entry => entry.Kind == GameEventKind.Quest && entry.Text.Contains("Completed the quest"));
    }

    [Fact]
    public void ADialogueMoveStillLogsWhatItAlwaysDid()
    {
        var state = new GameState();
        var context = new DialogueContext { State = state, LogWarning = _ => { } };

        DialogueEffects.Run(EffectRef.Parse($"set_quest:{FetchQuest}:InProgress"), context);

        Assert.Contains(state.EventLog.Entries,
            entry => entry.Kind == GameEventKind.Quest && entry.Text.Contains("Started the quest"));
    }

    // --- The state ladder ----------------------------------------------------

    [Fact]
    public void AdvancingWalksUnstartedToInProgressToSuccess()
    {
        var state = new GameState();

        Assert.True(QuestManager.AdvanceState(state, FetchQuest));
        Assert.Equal(QUESTSUCCESSSTATE.InProgress, state.GetQuestState(FetchQuest));

        Assert.True(QuestManager.AdvanceState(state, FetchQuest));
        Assert.Equal(QUESTSUCCESSSTATE.Success, state.GetQuestState(FetchQuest));

        Assert.False(QuestManager.AdvanceState(state, FetchQuest));
    }

    [Fact]
    public void CompletingAndFailingAreTheirOwnVerbs()
    {
        var completed = new GameState();
        QuestManager.StartQuest(completed, FetchQuest);
        Assert.True(QuestManager.CompleteQuest(completed, FetchQuest));
        Assert.Equal(QUESTSUCCESSSTATE.Success, completed.GetQuestState(FetchQuest));

        var failed = new GameState();
        QuestManager.StartQuest(failed, FetchQuest);
        Assert.True(QuestManager.FailQuest(failed, FetchQuest));
        Assert.Equal(QUESTSUCCESSSTATE.Failed, failed.GetQuestState(FetchQuest));
    }

    // --- Reaching a stage, the shape a world beat wants ----------------------

    [Fact]
    public void ReachingAStageOnlyEverMovesForward()
    {
        var state = new GameState();
        QuestManager.StartQuest(state, FetchQuest);
        Assert.Equal(1u, state.GetQuestStage(FetchQuest));

        Assert.True(QuestManager.ReachStage(state, FetchQuest, 2));
        Assert.Equal(2u, state.GetQuestStage(FetchQuest));

        // Walking back through the same trigger, or reloading a save made
        // after it, changes nothing — which is why no trigger has to remember
        // that it fired.
        Assert.False(QuestManager.ReachStage(state, FetchQuest, 2));
        Assert.False(QuestManager.ReachStage(state, FetchQuest, 1));
        Assert.Equal(2u, state.GetQuestStage(FetchQuest));
    }

    [Fact]
    public void ReachingAStageDoesNothingForAQuestNotBeingPlayed()
    {
        var unstarted = new GameState();
        Assert.False(QuestManager.ReachStage(unstarted, FetchQuest, 2));
        Assert.Equal(0u, unstarted.GetQuestStage(FetchQuest));

        var finished = new GameState();
        QuestManager.StartQuest(finished, FetchQuest);
        QuestManager.CompleteQuest(finished, FetchQuest);
        Assert.False(QuestManager.ReachStage(finished, FetchQuest, 2));
    }

    // --- Events --------------------------------------------------------------

    [Fact]
    public void EveryMoveIsAnnounced()
    {
        var state = new GameState();

        var moves = Watch(state, () =>
        {
            QuestManager.SetState(state, BountyQuest, QUESTSUCCESSSTATE.InProgress);
            QuestManager.SetState(state, BountyQuest, QUESTSUCCESSSTATE.Success);
        });

        Assert.Equal(
            new[] { QuestMoveKind.Started, QuestMoveKind.Completed },
            moves.Select(move => move.Kind));
        Assert.Equal(QUESTSUCCESSSTATE.Unstarted, moves[0].FromState);
        Assert.Equal(QUESTSUCCESSSTATE.InProgress, moves[0].ToState);
        Assert.Equal(BountyQuest, moves[0].QuestId);
    }

    [Fact]
    public void AStageMoveIsAnnouncedWithWhereItCameFrom()
    {
        var state = new GameState();
        QuestManager.StartQuest(state, FetchQuest);

        var move = Assert.Single(Watch(state, () => QuestManager.AdvanceStage(state, FetchQuest)));

        Assert.Equal(QuestMoveKind.StageChanged, move.Kind);
        Assert.Equal(1u, move.FromStage);
        Assert.Equal(2u, move.ToStage);
        Assert.Equal(QUESTSUCCESSSTATE.InProgress, move.ToState);
        Assert.Equal(QuestCatalog.Get(FetchQuest).Title, move.Quest.Title);
    }

    [Fact]
    public void TakingAQuestAnnouncesTheStageItLandedOn()
    {
        // A subscriber shouldn't need a second event to learn that a quest just
        // started sits on stage 1.
        var state = new GameState();

        var move = Assert.Single(Watch(state, () => QuestManager.StartQuest(state, FetchQuest)));

        Assert.Equal(QuestMoveKind.Started, move.Kind);
        Assert.Equal(0u, move.FromStage);
        Assert.Equal(1u, move.ToStage);
    }

    [Fact]
    public void NothingIsAnnouncedForAMoveThatDidNotHappen()
    {
        var state = new GameState();
        QuestManager.SetState(state, FetchQuest, QUESTSUCCESSSTATE.Success);

        var moves = Watch(state, () =>
        {
            QuestManager.SetState(state, FetchQuest, QUESTSUCCESSSTATE.Success);
            // The fetch quest declares two stages, so there is no third.
            QuestManager.SetStage(state, FetchQuest, 3);
        });

        Assert.Empty(moves);
    }
}
