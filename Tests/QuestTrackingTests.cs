using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Xunit;

// quest-markers.md Phase 3: the tracked quest — the one whose markers the map
// draws. It is state, not UI, so the rules and the save round-trip are testable
// without the engine; the Quests tab that sets it is Godot-facing.
public class QuestTrackingTests : IDisposable
{
    private const uint FetchQuest = QuestCatalog.ReturnTheMaguffinId;
    private const uint BountyQuest = QuestCatalog.ClearTheDeckId;

    private readonly string root = Path.Combine(Path.GetTempPath(), $"spacerpg-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void NothingIsTrackedInANewGame()
    {
        Assert.Equal(0u, new GameState().TrackedQuestId);
    }

    [Fact]
    public void OnlyAnInProgressQuestCanBeTracked()
    {
        var state = new GameState();

        Assert.False(state.SetTrackedQuest(FetchQuest));
        Assert.Equal(0u, state.TrackedQuestId);

        state.SetQuestState(FetchQuest, QUESTSUCCESSSTATE.InProgress);

        Assert.True(state.SetTrackedQuest(FetchQuest));
        Assert.Equal(FetchQuest, state.TrackedQuestId);
    }

    [Fact]
    public void FinishingTheTrackedQuestClearsTracking()
    {
        var state = new GameState();
        state.SetQuestState(FetchQuest, QUESTSUCCESSSTATE.InProgress);
        state.SetTrackedQuest(FetchQuest);

        state.SetQuestState(FetchQuest, QUESTSUCCESSSTATE.Success);

        Assert.Equal(0u, state.TrackedQuestId);
    }

    [Fact]
    public void FinishingAnUntrackedQuestLeavesTrackingAlone()
    {
        var state = new GameState();
        state.SetQuestState(FetchQuest, QUESTSUCCESSSTATE.InProgress);
        state.SetQuestState(BountyQuest, QUESTSUCCESSSTATE.InProgress);
        state.SetTrackedQuest(FetchQuest);

        state.SetQuestState(BountyQuest, QUESTSUCCESSSTATE.Success);

        Assert.Equal(FetchQuest, state.TrackedQuestId);
    }

    [Fact]
    public void TrackingCanBeCleared()
    {
        var state = new GameState();
        state.SetQuestState(FetchQuest, QUESTSUCCESSSTATE.InProgress);
        state.SetTrackedQuest(FetchQuest);

        Assert.True(state.SetTrackedQuest(0));
        Assert.Equal(0u, state.TrackedQuestId);
    }

    [Fact]
    public void ChangesAreAnnouncedOnceEach()
    {
        var state = new GameState();
        state.SetQuestState(FetchQuest, QUESTSUCCESSSTATE.InProgress);
        state.SetQuestState(BountyQuest, QUESTSUCCESSSTATE.InProgress);
        var announced = new List<uint>();
        void Listener(uint questId) => announced.Add(questId);
        QuestTracking.Changed += Listener;
        try
        {
            // Already tracking the fetch quest (taking it started that), so
            // this is not a change; the views that rebuild on the signal
            // shouldn't churn.
            state.SetTrackedQuest(FetchQuest);
            state.SetTrackedQuest(BountyQuest);
            // Finishing the tracked quest hands over to the one still open.
            state.SetQuestState(BountyQuest, QUESTSUCCESSSTATE.Success);
            // And finishing that one leaves nothing to follow.
            state.SetQuestState(FetchQuest, QUESTSUCCESSSTATE.Success);
        }
        finally
        {
            QuestTracking.Changed -= Listener;
        }

        Assert.Equal(new[] { BountyQuest, FetchQuest, 0u }, announced);
    }

    [Fact]
    public void TakingAQuestWhileFollowingNoneTracksIt()
    {
        // The reason the map has anything on it for a player who never opened
        // the journal: taking a quest is enough.
        var state = new GameState();

        state.SetQuestState(FetchQuest, QUESTSUCCESSSTATE.InProgress);

        Assert.Equal(FetchQuest, state.TrackedQuestId);
    }

    [Fact]
    public void TakingASecondQuestDoesNotStealTracking()
    {
        var state = new GameState();
        state.SetQuestState(FetchQuest, QUESTSUCCESSSTATE.InProgress);

        state.SetQuestState(BountyQuest, QUESTSUCCESSSTATE.InProgress);

        Assert.Equal(FetchQuest, state.TrackedQuestId);
    }

    [Fact]
    public void FinishingTheTrackedQuestHandsOverToOneStillInProgress()
    {
        var state = new GameState();
        state.SetQuestState(BountyQuest, QUESTSUCCESSSTATE.InProgress);
        state.SetQuestState(FetchQuest, QUESTSUCCESSSTATE.InProgress);
        Assert.Equal(BountyQuest, state.TrackedQuestId);

        state.SetQuestState(BountyQuest, QUESTSUCCESSSTATE.Success);

        Assert.Equal(FetchQuest, state.TrackedQuestId);
    }

    [Fact]
    public void FinishingTheLastOpenQuestLeavesNothingTracked()
    {
        var state = new GameState();
        state.SetQuestState(FetchQuest, QUESTSUCCESSSTATE.InProgress);
        state.SetQuestState(BountyQuest, QUESTSUCCESSSTATE.InProgress);

        state.SetQuestState(FetchQuest, QUESTSUCCESSSTATE.Success);
        state.SetQuestState(BountyQuest, QUESTSUCCESSSTATE.Success);

        Assert.Equal(0u, state.TrackedQuestId);
    }

    // Handover prefers a main quest over a side quest, matching the journal's
    // own ordering. Not directly testable yet: with two shipped quests, a
    // handover never has more than one candidate to choose between.

    [Fact]
    public void UntrackingSticksUntilAnotherQuestMoves()
    {
        var state = new GameState();
        state.SetQuestState(FetchQuest, QUESTSUCCESSSTATE.InProgress);

        state.SetTrackedQuest(0);
        // Unrelated progress edits leave the player's choice alone.
        state.GetOrAddQuestProgress(FetchQuest).CurrentStageNumber = 2;

        Assert.Equal(0u, state.TrackedQuestId);
    }

    [Fact]
    public void TrackedQuestRoundTripsThroughSaveAndLoad()
    {
        var repository = new SaveRepository(root);
        var slotId = Guid.NewGuid();
        var state = new GameState
        {
            CurrentLevelPath = "res://Scenes/Levels/Intro.tscn",
            LocationName = "Intro Station",
            LocationId = Guid.NewGuid(),
        };
        state.SetQuestState(FetchQuest, QUESTSUCCESSSTATE.InProgress);
        state.SetTrackedQuest(FetchQuest);
        repository.Save(
            new SaveData { SlotId = slotId, SaveVersion = SaveRepository.CurrentSaveVersion }, state);

        var loaded = repository.LoadState(slotId);

        Assert.Equal(FetchQuest, loaded.TrackedQuestId);
    }

    [Fact]
    public void APreV10SaveLoadsWithNothingTracked()
    {
        // The field simply isn't in the JSON; the property default covers it,
        // which is why v10 needs no migration step.
        var repository = new SaveRepository(root);
        var slotId = Guid.NewGuid();
        var state = new GameState { CurrentLevelPath = "res://Scenes/Levels/Intro.tscn" };
        state.SetQuestState(FetchQuest, QUESTSUCCESSSTATE.InProgress);
        state.SetTrackedQuest(FetchQuest);
        repository.Save(new SaveData { SlotId = slotId, SaveVersion = 9 }, state);
        var statePath = Path.Combine(root, $"slot_{slotId:N}", "state.json");
        var json = JsonNode.Parse(File.ReadAllText(statePath)).AsObject();
        Assert.True(json.Remove("TrackedQuestId"));
        File.WriteAllText(statePath, json.ToJsonString());

        var loaded = repository.LoadState(slotId);

        Assert.Equal(0u, loaded.TrackedQuestId);
        Assert.Equal(QUESTSUCCESSSTATE.InProgress, loaded.GetQuestState(FetchQuest));
    }
}
