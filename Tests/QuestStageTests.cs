using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

// quest-system.md Phase 1: quest stages — the beats a quest is made of, the
// catalog validation that keeps a stage list usable, and the scripted verbs
// that move a quest along one. Stages are data and progress, so all of it is
// testable without the engine; the journal line that shows the current stage is
// Godot-facing.
public class QuestStageTests : IDisposable
{
    private const uint FetchQuest = QuestCatalog.ReturnTheMaguffinId;

    private readonly string root = Path.Combine(Path.GetTempPath(), $"spacerpg-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static DialogueContext Context(GameState state, out List<string> warnings)
    {
        var captured = new List<string>();
        warnings = captured;
        return new DialogueContext { State = state, LogWarning = captured.Add };
    }

    private static Quest Staged(params QuestStage[] stages) => new()
    {
        Id = 1,
        Title = "Test Quest",
        Stages = stages.ToList(),
    };

    // --- Catalog validation --------------------------------------------------

    [Fact]
    public void TheShippedCatalogIsSound()
    {
        // The static constructor throws on bad content, so reaching this line at
        // all is half the check; assert it explicitly so a failure reads as
        // "the catalog is wrong" rather than as a type-initializer error
        // somewhere else.
        Assert.Null(QuestCatalog.Problems(QuestCatalog.All));
    }

    [Fact]
    public void EveryShippedQuestWithStagesNumbersThemFromOne()
    {
        foreach (var quest in QuestCatalog.All.Where(quest => quest.HasStages))
        {
            Assert.Equal(1u, quest.FirstStageNumber);
            Assert.Equal(
                Enumerable.Range(1, quest.Stages.Count).Select(n => (uint)n),
                quest.Stages.Select(stage => stage.StageNumber));
        }
    }

    [Fact]
    public void AQuestWithNoStagesIsValid()
    {
        // Every quest was this until stages landed, and a quest whose beats are
        // carried entirely by its markers still is.
        Assert.Null(QuestCatalog.Problems(new[] { Staged() }));
    }

    [Theory]
    // A hole: the journal would show "stage 2 of 2" for a beat nothing can
    // reach, and `stage next` would step off the end early.
    [InlineData(1u, 3u)]
    // Out of order.
    [InlineData(2u, 1u)]
    // Repeated.
    [InlineData(1u, 1u)]
    // Numbered from zero, which is the value that means "no stage".
    [InlineData(0u, 1u)]
    public void StageNumbersMustRunOneToNInOrder(uint first, uint second)
    {
        var problems = QuestCatalog.Problems(new[]
        {
            Staged(QuestStage.Create(first, "First"), QuestStage.Create(second, "Second")),
        });
        Assert.Contains("stage numbers run 1..n in order", problems);
    }

    [Fact]
    public void AStageNeedsASubtitle()
    {
        var problems = QuestCatalog.Problems(new[] { Staged(QuestStage.Create(1, "  ")) });
        Assert.Contains("no subtitle", problems);
    }

    // --- Prerequisite validation ---------------------------------------------

    private static Quest WithPrereqs(uint id, params uint[] prereqIds) => new()
    {
        Id = id,
        Title = $"Quest {id}",
        PrereqQuests = prereqIds
            .Select(prereqId => new QuestPrereqFlag
            {
                QuestId = prereqId,
                SuccessState = QUESTSUCCESSSTATE.Success,
            })
            .ToList(),
    };

    [Fact]
    public void APrerequisiteMayNameAnyQuestInTheSet()
    {
        // Including one registered after it: prerequisites are checked once the
        // whole catalog is built, so a forward reference is fine.
        Assert.Null(QuestCatalog.Problems(new[] { WithPrereqs(1, 2), WithPrereqs(2) }));
    }

    [Fact]
    public void APrerequisiteNamingAMissingQuestIsRejected()
    {
        var problems = QuestCatalog.Problems(new[] { WithPrereqs(1, 99) });
        Assert.Contains("prerequisite names quest 99, which doesn't exist", problems);
    }

    [Fact]
    public void AQuestCannotRequireItself()
    {
        var problems = QuestCatalog.Problems(new[] { WithPrereqs(1, 1) });
        Assert.Contains("names its own quest", problems);
    }

    [Fact]
    public void ARepeatedPrerequisiteIsRejected()
    {
        var problems = QuestCatalog.Problems(new[] { WithPrereqs(1, 2, 2), WithPrereqs(2) });
        Assert.Contains("twice", problems);
    }

    // --- Progress ------------------------------------------------------------

    [Fact]
    public void TakingAQuestPutsItOnItsFirstStage()
    {
        var state = new GameState();
        Assert.Equal(0u, state.GetQuestStage(FetchQuest));

        state.SetQuestState(FetchQuest, QUESTSUCCESSSTATE.InProgress);

        Assert.Equal(1u, state.GetQuestStage(FetchQuest));
        Assert.Equal(
            QuestCatalog.Get(FetchQuest).Stages[0].SubtitleText,
            state.GetCurrentStage(FetchQuest).SubtitleText);
    }

    [Fact]
    public void FinishingAQuestLeavesItsStageWhereItWas()
    {
        var state = new GameState();
        state.SetQuestState(FetchQuest, QUESTSUCCESSSTATE.InProgress);
        state.AdvanceQuestStage(FetchQuest);

        state.SetQuestState(FetchQuest, QUESTSUCCESSSTATE.Success);

        Assert.Equal(2u, state.GetQuestStage(FetchQuest));
    }

    [Fact]
    public void WindingAQuestBackToUnstartedDropsItsStage()
    {
        var state = new GameState();
        state.SetQuestState(FetchQuest, QUESTSUCCESSSTATE.InProgress);

        state.SetQuestState(FetchQuest, QUESTSUCCESSSTATE.Unstarted);

        Assert.Equal(0u, state.GetQuestStage(FetchQuest));
        Assert.Null(state.GetCurrentStage(FetchQuest));
    }

    [Fact]
    public void OnlyADeclaredStageCanBeSet()
    {
        var state = new GameState();
        var beyondLast = (uint)QuestCatalog.Get(FetchQuest).Stages.Count + 1;

        Assert.False(state.SetQuestStage(FetchQuest, beyondLast));
        Assert.False(state.SetQuestStage(FetchQuest, 0));
        Assert.Empty(state.Quests);

        Assert.True(state.SetQuestStage(FetchQuest, 2));
        Assert.Equal(2u, state.GetQuestStage(FetchQuest));
    }

    [Fact]
    public void AdvancingStopsAtTheLastStage()
    {
        var state = new GameState();
        var stageCount = (uint)QuestCatalog.Get(FetchQuest).Stages.Count;

        // From stage 0 — a quest carried by a save written before stages
        // existed — the first advance rejoins the ladder at stage 1.
        for (var expected = 1u; expected <= stageCount; expected++)
        {
            Assert.True(state.AdvanceQuestStage(FetchQuest));
            Assert.Equal(expected, state.GetQuestStage(FetchQuest));
        }

        Assert.False(state.AdvanceQuestStage(FetchQuest));
        Assert.Equal(stageCount, state.GetQuestStage(FetchQuest));
    }

    [Fact]
    public void TheStageRoundTripsThroughSaveAndLoad()
    {
        var repository = new SaveRepository(root);
        var slotId = Guid.NewGuid();
        var state = new GameState { CurrentLevelPath = "res://Scenes/Levels/Intro.tscn" };
        state.SetQuestState(FetchQuest, QUESTSUCCESSSTATE.InProgress);
        state.AdvanceQuestStage(FetchQuest);
        repository.Save(
            new SaveData { SlotId = slotId, SaveVersion = SaveRepository.CurrentSaveVersion }, state);

        var loaded = repository.LoadState(slotId);

        Assert.Equal(2u, loaded.GetQuestStage(FetchQuest));
        Assert.Equal(
            QuestCatalog.Get(FetchQuest).Stages[1].SubtitleText,
            loaded.GetCurrentStage(FetchQuest).SubtitleText);
    }

    [Fact]
    public void AQuestTakenBeforeStagesExistedLoadsOnNoStageAndRejoins()
    {
        // CurrentStageNumber has been in the save format since quests were, and
        // every quest wrote 0 into it until now. Such a save needs no migration:
        // it loads on "no stage", and the first thing to move the quest puts it
        // on stage 1.
        var repository = new SaveRepository(root);
        var slotId = Guid.NewGuid();
        var state = new GameState { CurrentLevelPath = "res://Scenes/Levels/Intro.tscn" };
        state.Quests.Add(new QuestProgress
        {
            QuestId = FetchQuest,
            State = QUESTSUCCESSSTATE.InProgress,
            CurrentStageNumber = 0,
        });
        repository.Save(
            new SaveData { SlotId = slotId, SaveVersion = SaveRepository.CurrentSaveVersion }, state);

        var loaded = repository.LoadState(slotId);

        Assert.Equal(0u, loaded.GetQuestStage(FetchQuest));
        Assert.Null(loaded.GetCurrentStage(FetchQuest));

        Assert.True(loaded.AdvanceQuestStage(FetchQuest));
        Assert.Equal(1u, loaded.GetQuestStage(FetchQuest));
    }

    // --- quest_stage(id), the condition vocabulary's way in -------------------

    [Fact]
    public void QuestStageReadsTheStageTheQuestIsOn()
    {
        var state = new GameState();
        var context = Context(state, out var warnings);

        // A quest not yet taken reads 0, so a stage gate needs no quest_state
        // guard beside it.
        Assert.False(DialogueConditions.Evaluate(ConditionRef.Parse("quest_stage:1:>=:1"), context));

        state.SetQuestState(FetchQuest, QUESTSUCCESSSTATE.InProgress);
        Assert.True(DialogueConditions.Evaluate(ConditionRef.Parse("quest_stage:1:>=:1"), context));
        Assert.False(DialogueConditions.Evaluate(ConditionRef.Parse("quest_stage:1:>=:2"), context));

        state.AdvanceQuestStage(FetchQuest);
        Assert.True(DialogueConditions.Evaluate(ConditionRef.Parse("quest_stage:1:>=:2"), context));
        Assert.True(DialogueConditions.Evaluate(ConditionRef.Parse("quest_stage:1:==:2"), context));

        Assert.Empty(warnings);
    }

    [Fact]
    public void QuestStageIsPartOfTheConditionVocabulary()
    {
        // The Yarn compiler and the editor's verb dropdown read
        // DialogueConditions.Ids; a query missing from it would be a verb no
        // conversation could name.
        Assert.Contains("quest_stage", DialogueConditions.Ids);
        Assert.Contains("quest_stage", DialogueQueries.Ids);
        Assert.Equal(DialogueQueries.ValueKind.Number, DialogueQueries.KindOf("quest_stage"));
    }

    [Theory]
    [InlineData("quest_stage:>=:1", "compared to a value")]
    [InlineData("quest_stage:abc:>=:1", "is not a whole number")]
    [InlineData("quest_stage:99999:>=:1", "no quest with id 99999")]
    public void QuestStageRejectsNonsense(string token, string expected)
    {
        Assert.Contains(expected, DialogueConditions.Validate(ConditionRef.Parse(token)));
    }

    // --- set_stage, the scripted way to move one -----------------------------

    [Fact]
    public void SetStageMovesTheQuestAndSaysSo()
    {
        var state = new GameState();
        state.SetQuestState(FetchQuest, QUESTSUCCESSSTATE.InProgress);
        var context = Context(state, out var warnings);

        DialogueEffects.Run(EffectRef.Parse("set_stage:1:2"), context);

        Assert.Equal(2u, state.GetQuestStage(FetchQuest));
        Assert.Empty(warnings);
        // The new objective is news the same way a quest move is: logged, and
        // toasted by the generic event layer.
        var entry = state.EventLog.Entries.Last();
        Assert.Equal(GameEventKind.Quest, entry.Kind);
        Assert.Contains(QuestCatalog.Get(FetchQuest).Stages[1].SubtitleText, entry.Text);
        Assert.True(entry.Notify);
    }

    [Fact]
    public void SetStageNextStepsOne()
    {
        var state = new GameState();
        state.SetQuestState(FetchQuest, QUESTSUCCESSSTATE.InProgress);
        var context = Context(state, out var warnings);

        DialogueEffects.Run(EffectRef.Parse("set_stage:1:next"), context);

        Assert.Equal(2u, state.GetQuestStage(FetchQuest));
        Assert.Empty(warnings);
    }

    [Fact]
    public void SetStageWarnsRatherThanMovingWhenItCannot()
    {
        var state = new GameState();
        state.SetQuestState(FetchQuest, QUESTSUCCESSSTATE.InProgress);
        var stageCount = (uint)QuestCatalog.Get(FetchQuest).Stages.Count;
        state.SetQuestStage(FetchQuest, stageCount);
        var context = Context(state, out var warnings);

        // Replaying the conversation that ends the ladder shouldn't look like
        // it did something.
        DialogueEffects.Run(EffectRef.Parse("set_stage:1:next"), context);

        Assert.Equal(stageCount, state.GetQuestStage(FetchQuest));
        Assert.Single(warnings);
    }

    [Theory]
    [InlineData("set_stage:1", "takes <questId>")]
    [InlineData("set_stage:abc:1", "is not a whole number")]
    [InlineData("set_stage:99999:1", "no quest with id 99999")]
    [InlineData("set_stage:1:0", "has no stage 0")]
    [InlineData("set_stage:1:9", "has no stage 9")]
    [InlineData("set_stage:1:soon", "is not a whole number or 'next'")]
    public void SetStageIsValidatedAtAuthorTime(string token, string expected)
    {
        Assert.Contains(expected, DialogueEffects.Validate(EffectRef.Parse(token)));
    }

    [Fact]
    public void SetStageAcceptsWhatTheCatalogDeclares()
    {
        Assert.Null(DialogueEffects.Validate(EffectRef.Parse("set_stage:1:1")));
        Assert.Null(DialogueEffects.Validate(EffectRef.Parse("set_stage:1:next")));
        Assert.Contains("set_stage", DialogueEffects.Ids);
    }
}
