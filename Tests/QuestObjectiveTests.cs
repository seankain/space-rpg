using System.Collections.Generic;
using System.Linq;
using Xunit;

// quest-system.md Phase 3: the two things that point a player at a quest
// without opening a menu — the HUD's one-line "what am I doing", and which
// NPCs have something to offer. Both are decided here, engine-free; the label
// over the world and the mark over an NPC's head are thin.
public class QuestObjectiveTests
{
    private const uint FetchQuest = QuestCatalog.ReturnTheMaguffinId;
    private const uint BountyQuest = QuestCatalog.ClearTheDeckId;

    private static GameState Playing(uint questId)
    {
        var state = new GameState();
        QuestManager.StartQuest(state, questId);
        return state;
    }

    // --- The line the HUD shows -----------------------------------------------

    [Fact]
    public void TheObjectiveIsTheStageTheQuestIsOn()
    {
        var state = Playing(FetchQuest);
        var stages = QuestCatalog.Get(FetchQuest).Stages;

        Assert.Equal(stages[0].SubtitleText, QuestObjectives.CurrentFor(state, FetchQuest));

        QuestManager.AdvanceStage(state, FetchQuest);

        Assert.Equal(stages[1].SubtitleText, QuestObjectives.CurrentFor(state, FetchQuest));
    }

    [Fact]
    public void AQuestWithNoStagesFallsBackToItsMarkers()
    {
        // What every quest looked like before stages, and what one whose beats
        // are carried entirely by marker conditions still looks like.
        var quest = new Quest
        {
            Id = FetchQuest,
            Title = "Markers Only",
            Markers =
            {
                QuestMarker.Create(
                    QuestMarkerTarget.Item(ItemCatalog.MaguffinCubeId),
                    "Find the cube",
                    $"!has_item:{ItemCatalog.MaguffinCubeId}"),
                QuestMarker.Create(
                    QuestMarkerTarget.Npc("intro.dockmaster_hale"),
                    "Take it back",
                    $"has_item:{ItemCatalog.MaguffinCubeId}"),
            },
        };
        var state = Playing(FetchQuest);

        // Only the marker whose condition holds right now.
        Assert.Equal("Find the cube", QuestObjectives.CurrentFor(state, quest));

        state.Inventory.Add(ItemCatalog.MaguffinCubeId, 1);

        Assert.Equal("Take it back", QuestObjectives.CurrentFor(state, quest));
    }

    [Fact]
    public void TwoLiveMarkersAreJoinedRatherThanPicked()
    {
        var quest = new Quest
        {
            Id = FetchQuest,
            Title = "Two At Once",
            Markers =
            {
                QuestMarker.Create(QuestMarkerTarget.Npc("intro.vex"), "One"),
                QuestMarker.Create(QuestMarkerTarget.Npc("intro.rig"), "Two"),
            },
        };

        Assert.Equal(
            "One" + QuestObjectives.Separator + "Two",
            QuestObjectives.CurrentFor(Playing(FetchQuest), quest));
    }

    [Fact]
    public void AQuestNotBeingPlayedHasNothingToSay()
    {
        Assert.Null(QuestObjectives.CurrentFor(new GameState(), FetchQuest));

        var finished = Playing(FetchQuest);
        QuestManager.CompleteQuest(finished, FetchQuest);
        Assert.Null(QuestObjectives.CurrentFor(finished, FetchQuest));

        Assert.Null(QuestObjectives.CurrentFor(null, FetchQuest));
    }

    [Fact]
    public void TheHeadlineFollowsTheTrackedQuest()
    {
        // Taking a quest while following none tracks it, so the HUD has
        // something to say without the player opening the journal.
        var state = Playing(FetchQuest);

        var headline = QuestObjectives.Tracked(state);

        Assert.Equal(FetchQuest, headline.QuestId);
        Assert.Equal(QuestCatalog.Get(FetchQuest).Title, headline.Title);
        Assert.Equal(QuestCatalog.Get(FetchQuest).Stages[0].SubtitleText, headline.Objective);
    }

    [Fact]
    public void NothingTrackedIsNothingToDraw()
    {
        Assert.Null(QuestObjectives.Tracked(new GameState()));
        Assert.Null(QuestObjectives.Tracked(null));

        var state = Playing(FetchQuest);
        state.SetTrackedQuest(0);
        Assert.Null(QuestObjectives.Tracked(state));
    }

    [Fact]
    public void TrackingSurvivesIntoTheHeadlineAfterAHandover()
    {
        // Finishing the tracked quest hands over to one still in progress; the
        // HUD should follow it rather than blank.
        var state = new GameState();
        QuestManager.StartQuest(state, FetchQuest);
        QuestManager.StartQuest(state, BountyQuest);
        QuestManager.CompleteQuest(state, FetchQuest);

        var headline = QuestObjectives.Tracked(state);

        Assert.Equal(BountyQuest, headline.QuestId);
        Assert.Equal(QuestCatalog.Get(BountyQuest).Stages[0].SubtitleText, headline.Objective);
    }

    // --- Who has a quest to give ---------------------------------------------

    [Fact]
    public void EveryShippedQuestNamesAGiverThatIsAnNpcId()
    {
        // The id itself is checked against the definitions on disk by
        // QuestMarkerContentTests, which is the only place that can see them.
        foreach (var quest in QuestCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(quest.GiverNpcId));
        }
    }

    [Fact]
    public void AGiverOffersTheirUnstartedQuest()
    {
        var state = new GameState();
        var hale = QuestCatalog.Get(FetchQuest).GiverNpcId;

        var offered = QuestManager.AvailableFrom(state, hale);

        Assert.Equal(new[] { FetchQuest }, offered.Select(quest => quest.Id));
    }

    [Fact]
    public void AQuestAlreadyTakenIsNoLongerOnOffer()
    {
        var state = new GameState();
        var hale = QuestCatalog.Get(FetchQuest).GiverNpcId;

        QuestManager.StartQuest(state, FetchQuest);
        Assert.Empty(QuestManager.AvailableFrom(state, hale));

        // And a finished one doesn't come back.
        QuestManager.CompleteQuest(state, FetchQuest);
        Assert.Empty(QuestManager.AvailableFrom(state, hale));
    }

    [Fact]
    public void AnNpcWhoGivesNothingOffersNothing()
    {
        var state = new GameState();
        Assert.Empty(QuestManager.AvailableFrom(state, "intro.vex"));
        Assert.Empty(QuestManager.AvailableFrom(state, ""));
        Assert.Empty(QuestManager.AvailableFrom(state, null));
        Assert.Empty(QuestManager.AvailableFrom(null, "intro.dockmaster_hale"));
    }

    [Fact]
    public void GiversAreMatchedExactly()
    {
        // Not a prefix, not case-insensitively: NpcIds are keys, and two
        // stations could easily hold an "intro.chief_marlow" and an
        // "intro.chief_marlow_deputy".
        var state = new GameState();
        var marlow = QuestCatalog.Get(BountyQuest).GiverNpcId;

        Assert.Empty(QuestManager.AvailableFrom(state, marlow.ToUpperInvariant()));
        Assert.Empty(QuestManager.AvailableFrom(state, marlow + "_deputy"));
        Assert.NotEmpty(QuestManager.AvailableFrom(state, marlow));
    }

    // --- Catalog validation of the link --------------------------------------

    [Theory]
    [InlineData("   ", "giver npc id is blank")]
    [InlineData("intro:vex", "can't contain ':'")]
    public void ABrokenGiverIsRejectedAtRegistration(string giver, string expected)
    {
        var problems = QuestCatalog.Problems(new List<Quest>
        {
            new() { Id = 1, Title = "Test Quest", GiverNpcId = giver },
        });

        Assert.Contains(expected, problems);
    }

    [Fact]
    public void AQuestNobodyGivesIsValid()
    {
        // A quest started by a trigger or by another quest finishing has no
        // giver, and shouldn't have to invent one.
        Assert.Null(QuestCatalog.Problems(new List<Quest>
        {
            new() { Id = 1, Title = "Test Quest", GiverNpcId = "" },
        }));
    }
}
