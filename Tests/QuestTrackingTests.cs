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
            state.SetTrackedQuest(FetchQuest);
            // Re-tracking the same quest is not a change; the views that
            // rebuild on this signal shouldn't churn.
            state.SetTrackedQuest(FetchQuest);
            state.SetTrackedQuest(BountyQuest);
            // Completing the tracked quest clears tracking, which is a change.
            state.SetQuestState(BountyQuest, QUESTSUCCESSSTATE.Success);
        }
        finally
        {
            QuestTracking.Changed -= Listener;
        }

        Assert.Equal(new[] { FetchQuest, BountyQuest, 0u }, announced);
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
