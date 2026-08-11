using System.Collections.Generic;
using System.Linq;
using Xunit;

// In-game-editor plan Phase 5 acceptance: the engine-free quest commands run
// against a fabricated GameState — state transitions, stage writes, string->enum
// parsing, and unknown-id / bad-state rejection. The Godot wrappers (which only
// fetch SaveManager.Instance.CurrentState) are exercised in the running editor.
public class EditorQuestCommandTests
{
    private const uint QuestId = QuestCatalog.ReturnTheMaguffinId; // 1

    private static GameState NewState() => new();

    private static IReadOnlyList<string> Args(params string[] a) => a;

    private static uint StageOf(GameState state, uint questId) =>
        state.Quests.First(q => q.QuestId == questId).CurrentStageNumber;

    [Fact]
    public void StartSetsInProgressAndCreatesProgress()
    {
        var state = NewState();
        var result = EditorQuestCommands.Run(state, Args("start", QuestId.ToString()));
        Assert.True(result.Success);
        Assert.Equal(QUESTSUCCESSSTATE.InProgress, state.GetQuestState(QuestId));
    }

    [Theory]
    [InlineData("success", QUESTSUCCESSSTATE.Success)]
    [InlineData("SUCCESS", QUESTSUCCESSSTATE.Success)]
    [InlineData("Failed", QUESTSUCCESSSTATE.Failed)]
    [InlineData("inprogress", QUESTSUCCESSSTATE.InProgress)]
    [InlineData("unstarted", QUESTSUCCESSSTATE.Unstarted)]
    public void SetParsesStateCaseInsensitively(string token, QUESTSUCCESSSTATE expected)
    {
        var state = NewState();
        var result = EditorQuestCommands.Run(state, Args("set", QuestId.ToString(), token));
        Assert.True(result.Success);
        Assert.Equal(expected, state.GetQuestState(QuestId));
    }

    [Fact]
    public void SetRejectsBadState()
    {
        var state = NewState();
        state.SetQuestState(QuestId, QUESTSUCCESSSTATE.InProgress);
        var result = EditorQuestCommands.Run(state, Args("set", QuestId.ToString(), "bogus"));
        Assert.False(result.Success);
        // Unchanged.
        Assert.Equal(QUESTSUCCESSSTATE.InProgress, state.GetQuestState(QuestId));
    }

    [Fact]
    public void StageWritesCurrentStageNumber()
    {
        var state = NewState();
        var result = EditorQuestCommands.Run(state, Args("stage", QuestId.ToString(), "2"));
        Assert.True(result.Success);
        Assert.Equal(2u, StageOf(state, QuestId));
    }

    // Stage numbers are bounded by the definition now that quests declare
    // stages (quest-system.md Phase 1): a typo is refused rather than written.
    [Fact]
    public void StageRejectsAStageTheQuestDoesNotHave()
    {
        var state = NewState();
        var beyondLast = (uint)QuestCatalog.Get(QuestId).Stages.Count + 1;
        var result = EditorQuestCommands.Run(state, Args("stage", QuestId.ToString(), beyondLast.ToString()));
        Assert.False(result.Success);
        Assert.Empty(state.Quests);
    }

    [Fact]
    public void StageNextStepsThroughTheStagesAndStopsAtTheLast()
    {
        var state = NewState();
        var stageCount = QuestCatalog.Get(QuestId).Stages.Count;

        for (var expected = 1; expected <= stageCount; expected++)
        {
            Assert.True(EditorQuestCommands.Run(state, Args("stage", QuestId.ToString(), "next")).Success);
            Assert.Equal((uint)expected, StageOf(state, QuestId));
        }

        // The last stage is the end of the ladder: finishing is a state move.
        Assert.False(EditorQuestCommands.Run(state, Args("stage", QuestId.ToString(), "next")).Success);
        Assert.Equal((uint)stageCount, StageOf(state, QuestId));
    }

    // `advance` moves the success state, matching the dialogue vocabulary's
    // advance_quest; stages moved with it before stages were real.
    [Fact]
    public void AdvanceStepsTheStateAndStopsAtSuccess()
    {
        var state = NewState();

        Assert.True(EditorQuestCommands.Run(state, Args("advance", QuestId.ToString())).Success);
        Assert.Equal(QUESTSUCCESSSTATE.InProgress, state.GetQuestState(QuestId));

        Assert.True(EditorQuestCommands.Run(state, Args("advance", QuestId.ToString())).Success);
        Assert.Equal(QUESTSUCCESSSTATE.Success, state.GetQuestState(QuestId));

        Assert.False(EditorQuestCommands.Run(state, Args("advance", QuestId.ToString())).Success);
        Assert.Equal(QUESTSUCCESSSTATE.Success, state.GetQuestState(QuestId));
    }

    [Fact]
    public void UnknownQuestIdIsRejected()
    {
        var state = NewState();
        Assert.False(EditorQuestCommands.Run(state, Args("start", "99999")).Success);
        Assert.Empty(state.Quests);
    }

    [Fact]
    public void NonNumericQuestIdIsRejected()
    {
        Assert.False(EditorQuestCommands.Run(NewState(), Args("start", "abc")).Success);
    }

    [Fact]
    public void UnknownSubcommandIsRejected()
    {
        Assert.False(EditorQuestCommands.Run(NewState(), Args("frobnicate", "1")).Success);
    }

    [Fact]
    public void CommandsRejectMissingGameState()
    {
        Assert.False(EditorQuestCommands.Run(null, Args("start", "1")).Success);
        Assert.False(EditorQuestCommands.List(null).Success);
    }

    [Fact]
    public void ListReportsStateAndStage()
    {
        var state = NewState();
        EditorQuestCommands.Run(state, Args("set", QuestId.ToString(), "success"));
        EditorQuestCommands.Run(state, Args("stage", QuestId.ToString(), "2"));
        var result = EditorQuestCommands.List(state);
        Assert.True(result.Success);
        Assert.Contains(QuestCatalog.Get(QuestId).Title, result.Message);
        Assert.Contains("Success", result.Message);
    }
}
