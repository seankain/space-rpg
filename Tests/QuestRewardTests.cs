using System.Collections.Generic;
using System.Linq;
using Xunit;

// quest-system.md Phase 4: what a quest pays, and the stages that reach
// themselves. Both exist because a quest can now be finished more than one way
// — a reward authored in one turn-in conversation is a reward the other route
// never gets, and a beat announced by a trigger is a beat the player can walk
// around.
public class QuestRewardTests
{
    private const uint FetchQuest = QuestCatalog.ReturnTheMaguffinId;
    private const uint BountyQuest = QuestCatalog.ClearTheDeckId;
    private const uint Keycard = ItemCatalog.MaintenanceKeycardId;
    private const uint Cube = ItemCatalog.MaguffinCubeId;

    private static GameState WithParty(int members = 2)
    {
        var state = new GameState();
        for (var i = 1; i <= members; i++)
        {
            state.Party.Add(new CharacterEntity
            {
                Id = (ulong)i,
                Name = $"Member {i}",
                Level = 1,
                ExperiencePoints = 0,
                EquipSlots = new CharacterEquipSlots(),
                Stats = new CharacterStats(),
                ActiveStatusEffects = new List<ActiveStatusEffect>(),
            });
        }
        return state;
    }

    // --- Rewards --------------------------------------------------------------

    [Fact]
    public void CompletingAQuestHandsOverItsReward()
    {
        var state = WithParty();
        var reward = QuestCatalog.Get(BountyQuest).Reward;
        var credits = state.Credits;
        QuestManager.StartQuest(state, BountyQuest);

        QuestManager.CompleteQuest(state, BountyQuest);

        Assert.Equal(1u, state.Inventory.CountOf(Keycard));
        Assert.Equal(credits + reward.Credits, state.Credits);
        Assert.All(state.Party, member => Assert.Equal(reward.ExperiencePoints, member.ExperiencePoints));
    }

    [Fact]
    public void TheRewardArrivesHoweverTheQuestWasFinished()
    {
        // The point of moving it off the turn-in conversation: the console, a
        // second turn-in line, or a trigger all pay the same.
        var viaConsole = WithParty();
        QuestManager.StartQuest(viaConsole, BountyQuest);
        EditorQuestCommands.Run(viaConsole, new[] { "set", BountyQuest.ToString(), "success" });
        Assert.Equal(1u, viaConsole.Inventory.CountOf(Keycard));

        var viaDialogue = WithParty();
        var context = new DialogueContext { State = viaDialogue, LogWarning = _ => { } };
        QuestManager.StartQuest(viaDialogue, BountyQuest);
        DialogueEffects.Run(EffectRef.Parse($"set_quest:{BountyQuest}:Success"), context);
        Assert.Equal(1u, viaDialogue.Inventory.CountOf(Keycard));
    }

    [Fact]
    public void EachPartOfTheRewardIsLoggedTheWayThatGainAlwaysIs()
    {
        var state = WithParty();
        QuestManager.StartQuest(state, BountyQuest);

        QuestManager.CompleteQuest(state, BountyQuest);

        var kinds = state.EventLog.Entries.Select(entry => entry.Kind).ToList();
        Assert.Contains(GameEventKind.Item, kinds);
        Assert.Contains(GameEventKind.Credits, kinds);
        Assert.Contains(GameEventKind.Party, kinds);
        // The quest's own line comes first: completed, then paid.
        var completed = state.EventLog.Entries.FindIndex(entry => entry.Text.Contains("Completed the quest"));
        var received = state.EventLog.Entries.FindIndex(entry => entry.Text.Contains("Received"));
        Assert.True(completed >= 0 && received > completed);
    }

    [Fact]
    public void NothingIsPaidForAQuestThatDidNotSucceed()
    {
        var failed = WithParty();
        QuestManager.StartQuest(failed, BountyQuest);
        QuestManager.FailQuest(failed, BountyQuest);
        Assert.Equal(0u, failed.Inventory.CountOf(Keycard));

        var running = WithParty();
        QuestManager.StartQuest(running, BountyQuest);
        Assert.Equal(0u, running.Inventory.CountOf(Keycard));
    }

    [Fact]
    public void AQuestThatPaysNothingIsFine()
    {
        // Rewarding nothing is the default; only an all-zero reward *object* is
        // a mistake, because it reads as an intent nobody carried out.
        Assert.Null(QuestCatalog.Problems(new List<Quest>
        {
            new() { Id = 1, Title = "Unpaid", Reward = null },
        }));
        Assert.Contains("grants nothing", QuestCatalog.Problems(new List<Quest>
        {
            new() { Id = 1, Title = "Unpaid", Reward = new QuestReward() },
        }));
    }

    [Theory]
    [InlineData(99999u, 1u, "which doesn't exist")]
    [InlineData(Keycard, 0u, "grants 0 of item")]
    public void ABrokenRewardIsRejectedAtRegistration(uint itemId, uint quantity, string expected)
    {
        var problems = QuestCatalog.Problems(new List<Quest>
        {
            new()
            {
                Id = 1,
                Title = "Broken",
                Reward = new QuestReward { Items = { QuestReward.Item(itemId, quantity) } },
            },
        });

        Assert.Contains(expected, problems);
    }

    // --- Stages that reach themselves ----------------------------------------

    [Fact]
    public void AStageWhoseConditionHoldsReachesItself()
    {
        // The fetch quest's second beat is holding the cube, so it is true
        // however the party came by it.
        var state = new GameState();
        QuestManager.StartQuest(state, FetchQuest);
        Assert.Equal(1u, state.GetQuestStage(FetchQuest));

        state.Inventory.Add(Cube, 1);

        Assert.Equal(1, QuestObjectiveWatcher.Evaluate(state));
        Assert.Equal(2u, state.GetQuestStage(FetchQuest));
    }

    [Fact]
    public void PickingTheCubeUpBeforeTakingTheQuestStillReachesTheBeat()
    {
        // The ordering the trigger-shaped hook could never catch: the cube is
        // already in the party's pockets when Hale hands the quest over.
        var state = new GameState();
        state.Inventory.Add(Cube, 1);

        QuestManager.StartQuest(state, FetchQuest);
        QuestObjectiveWatcher.Evaluate(state);

        Assert.Equal(2u, state.GetQuestStage(FetchQuest));
    }

    [Fact]
    public void BeatingVexReachesTheBountysSecondBeat()
    {
        var state = new GameState();
        QuestManager.StartQuest(state, BountyQuest);

        state.MarkNpcDefeated("intro.vex");
        QuestObjectiveWatcher.Evaluate(state);

        Assert.Equal(2u, state.GetQuestStage(BountyQuest));
    }

    [Fact]
    public void EvaluatingChangesNothingWhileTheConditionIsFalse()
    {
        var state = new GameState();
        QuestManager.StartQuest(state, FetchQuest);

        Assert.Equal(0, QuestObjectiveWatcher.Evaluate(state));
        Assert.Equal(1u, state.GetQuestStage(FetchQuest));
    }

    [Fact]
    public void AQuestNotInProgressIsLeftAlone()
    {
        var state = new GameState();
        state.Inventory.Add(Cube, 1);

        Assert.Equal(0, QuestObjectiveWatcher.Evaluate(state));
        Assert.Equal(0u, state.GetQuestStage(FetchQuest));
    }

    [Fact]
    public void AStageWithNoConditionStopsTheWalk()
    {
        // A beat something has to announce can't be stepped over by the one
        // after it happening to be true.
        var quest = new Quest
        {
            Id = FetchQuest,
            Title = "Three Beats",
            Stages =
            {
                QuestStage.Create(1, "First"),
                QuestStage.Create(2, "Scripted"),
                QuestStage.Create(3, "Third", reachedWhen: $"has_item:{Cube}"),
            },
        };
        var state = new GameState();
        QuestManager.StartQuest(state, FetchQuest);
        state.Inventory.Add(Cube, 1);

        Assert.Equal(0, QuestObjectiveWatcher.Evaluate(state, quest));
        Assert.Equal(1u, state.GetQuestStage(FetchQuest));
    }

    [Fact]
    public void SeveralBeatsThatAllHoldAreWalkedInOrder()
    {
        // A save written before stages existed, loaded after both beats already
        // happened: each objective is reached (and logged) in turn rather than
        // the last one being jumped to.
        var quest = new Quest
        {
            Id = FetchQuest,
            Title = "Two Conditions",
            Stages =
            {
                QuestStage.Create(1, "Has the cube", reachedWhen: $"has_item:{Cube}"),
                QuestStage.Create(2, "Vex is down", reachedWhen: "npc_defeated:intro.vex"),
            },
        };
        var state = new GameState();
        state.Quests.Add(new QuestProgress
        {
            QuestId = FetchQuest,
            State = QUESTSUCCESSSTATE.InProgress,
            CurrentStageNumber = 0,
        });
        state.Inventory.Add(Cube, 1);
        state.MarkNpcDefeated("intro.vex");

        Assert.Equal(2, QuestObjectiveWatcher.Evaluate(state, quest));
        Assert.Equal(2u, state.GetQuestStage(FetchQuest));
        Assert.Equal(2, state.EventLog.Entries.Count(entry => entry.Text.StartsWith("New objective")));
    }

    [Fact]
    public void ReachedStagesNeverGoBack()
    {
        var state = new GameState();
        QuestManager.StartQuest(state, FetchQuest);
        state.Inventory.Add(Cube, 1);
        QuestObjectiveWatcher.Evaluate(state);

        // Handing the cube over doesn't send the player back to "find it".
        state.Inventory.Remove(Cube, 1);
        QuestObjectiveWatcher.Evaluate(state);

        Assert.Equal(2u, state.GetQuestStage(FetchQuest));
    }

    [Fact]
    public void ABadStageConditionIsRejectedAtRegistration()
    {
        var problems = QuestCatalog.Problems(new List<Quest>
        {
            new()
            {
                Id = 1,
                Title = "Broken",
                Stages = { QuestStage.Create(1, "First", reachedWhen: "has_item:99999") },
            },
        });

        Assert.Contains("no item with id 99999", problems);
    }

    [Fact]
    public void TheWatcherFollowsTheEventLogOnceInstalled()
    {
        // Install is process-wide and idempotent, so this leaves the hook
        // pointing at a state nobody else asserts on; evaluating a finished
        // state again is a no-op.
        var state = new GameState();
        QuestManager.StartQuest(state, FetchQuest);
        state.Inventory.Add(Cube, 1);
        QuestObjectiveWatcher.Install(() => state);

        // Any logged moment is a moment worth re-asking after — here, the
        // pickup line the world would have written.
        state.RecordEvent(GameEventKind.Item, "Picked up Maguffin Cube.", notify: true);

        Assert.Equal(2u, state.GetQuestStage(FetchQuest));
    }
}
