using System.Collections.Generic;
using Xunit;

// quest-markers.md Phase 4: where a tracked quest's markers land on the map
// that is currently open — drawn in place, routed to the door leading to them,
// or named in the header because this map doesn't cover them. Engine-free, so
// the routing rule is tested rather than eyeballed at four zoom levels.
public class QuestMarkerPlacementTests
{
    private const string Area = "IntroStation";
    private const string LevelScene = "res://Scenes/Levels/Intro.tscn";
    private const string ShopScene = "res://Scenes/Levels/ShopInterior.tscn";
    private const uint FetchQuest = QuestCatalog.ReturnTheMaguffinId;
    private const uint BountyQuest = QuestCatalog.ClearTheDeckId;

    [Fact]
    public void ATargetInThisAreaIsDrawnWhereItStands()
    {
        var placement = Place(new QuestTargetLocation
        {
            AreaName = Area,
            ScenePath = LevelScene,
            WorldX = 12f,
            WorldZ = -4f,
        });

        Assert.Equal(QuestMarkerPlacementKind.OnMap, placement.Kind);
        Assert.True(placement.IsDrawn);
        Assert.Equal(12f, placement.WorldX);
        Assert.Equal(-4f, placement.WorldZ);
        Assert.Equal("Find it", placement.DisplayText);
    }

    [Fact]
    public void ATargetKnownOnlyByLevelSceneCountsAsThisArea()
    {
        // What an NpcDefinition lookup returns for an NPC in this level that
        // hasn't spawned: a scene path, no area name.
        var placement = Place(new QuestTargetLocation { ScenePath = LevelScene, WorldX = 3f, WorldZ = 5f });

        Assert.Equal(QuestMarkerPlacementKind.OnMap, placement.Kind);
    }

    [Fact]
    public void ATargetInsideAnInteriorRoutesToItsDoor()
    {
        var landmarks = new MapLandmarksFile();
        landmarks.Landmarks.Add(new MapLandmark
        {
            Type = MapLandmark.DoorType,
            Name = "Supply Shop",
            TargetScenePath = ShopScene,
            X = 20f,
            Z = 2f,
        });

        var placement = Place(
            new QuestTargetLocation { ScenePath = ShopScene, WorldX = 500f, WorldZ = 500f },
            landmarks);

        Assert.Equal(QuestMarkerPlacementKind.AtEntrance, placement.Kind);
        Assert.True(placement.IsDrawn);
        // Drawn at the door, not at the target's own coordinates.
        Assert.Equal(20f, placement.WorldX);
        Assert.Equal(2f, placement.WorldZ);
        Assert.Equal("Find it — inside Supply Shop", placement.DisplayText);
    }

    [Fact]
    public void ATargetInAnotherAreaIsNamedRatherThanDrawn()
    {
        var placement = Place(new QuestTargetLocation
        {
            AreaName = "World1",
            ScenePath = "res://Scenes/Levels/World1.tscn",
            WorldX = 64f,
            WorldZ = 64f,
        });

        Assert.Equal(QuestMarkerPlacementKind.Elsewhere, placement.Kind);
        Assert.False(placement.IsDrawn);
        Assert.Equal("Find it — in World1", placement.DisplayText);
    }

    [Fact]
    public void AnInteriorWithNoDoorOnThisMapFallsBackToItsSceneName()
    {
        // The area has never been baked, so no door landmark records where the
        // shop is reached from.
        var placement = Place(new QuestTargetLocation { ScenePath = ShopScene });

        Assert.Equal(QuestMarkerPlacementKind.Elsewhere, placement.Kind);
        Assert.Equal("Find it — in ShopInterior", placement.DisplayText);
    }

    [Fact]
    public void AnUnlocatableTargetSaysSoInsteadOfVanishing()
    {
        var placement = Place(location: null);

        Assert.Equal(QuestMarkerPlacementKind.Elsewhere, placement.Kind);
        Assert.False(placement.IsDrawn);
        Assert.Equal("Find it — not on this map", placement.DisplayText);
    }

    [Fact]
    public void NothingTrackedPlacesNothing()
    {
        var state = new GameState();
        state.SetQuestState(FetchQuest, QUESTSUCCESSSTATE.InProgress);
        // Taking a quest tracks it, so untrack explicitly to get the
        // "following nothing" case.
        state.SetTrackedQuest(0);

        Assert.Empty(QuestMarkerPlacements.ForTrackedQuest(
            state, new FakeLocator(), Area, LevelScene, new MapLandmarksFile()));
        Assert.Empty(QuestMarkerPlacements.ForTrackedQuest(
            null, new FakeLocator(), Area, LevelScene, new MapLandmarksFile()));
    }

    [Fact]
    public void TheTrackedQuestsLiveMarkersArePlaced()
    {
        var state = new GameState();
        state.SetQuestState(BountyQuest, QUESTSUCCESSSTATE.InProgress);
        state.SetTrackedQuest(BountyQuest);
        var locator = new FakeLocator
        {
            ["npc:intro.vex"] = new QuestTargetLocation { AreaName = Area, WorldX = 8f, WorldZ = 9f },
        };

        var placements = QuestMarkerPlacements.ForTrackedQuest(
            state, locator, Area, LevelScene, new MapLandmarksFile());

        var placement = Assert.Single(placements);
        Assert.Equal("Deal with Vex", placement.Label);
        Assert.Equal(QuestMarkerPlacementKind.OnMap, placement.Kind);
        // A side quest, so the map draws it in the side-quest colour.
        Assert.True(placement.SideQuest);
        Assert.Equal(BountyQuest, placement.QuestId);
    }

    [Fact]
    public void PlacementsFollowTheQuestAsItProgresses()
    {
        var state = new GameState();
        state.SetQuestState(BountyQuest, QUESTSUCCESSSTATE.InProgress);
        state.SetTrackedQuest(BountyQuest);
        var locator = new FakeLocator
        {
            ["npc:intro.vex"] = new QuestTargetLocation { AreaName = Area },
            ["npc:intro.chief_marlow"] = new QuestTargetLocation { AreaName = Area, WorldX = 1f },
        };

        state.MarkNpcDefeated("intro.vex");
        var placements = QuestMarkerPlacements.ForTrackedQuest(
            state, locator, Area, LevelScene, new MapLandmarksFile());

        Assert.Equal("Report back to Chief Marlow", Assert.Single(placements).Label);
    }

    [Theory]
    [InlineData("res://Scenes/Levels/ShopInterior.tscn", "ShopInterior")]
    [InlineData("ShopInterior.tscn", "ShopInterior")]
    [InlineData("res://Scenes/Levels/NoExtension", "NoExtension")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void SceneNameIsTheReadablePartOfAPath(string path, string expected) =>
        Assert.Equal(expected, QuestMarkerPlacements.SceneName(path));

    private static QuestMarkerPlacement Place(QuestTargetLocation location, MapLandmarksFile landmarks = null) =>
        QuestMarkerPlacements.For(
            QuestCatalog.Get(FetchQuest),
            QuestMarker.Create(QuestMarkerTarget.Item(ItemCatalog.MaguffinCubeId), "Find it"),
            location,
            Area,
            LevelScene,
            landmarks ?? new MapLandmarksFile());

    private sealed class FakeLocator : IQuestTargetLocator
    {
        private readonly Dictionary<string, QuestTargetLocation> locations = new();

        public QuestTargetLocation this[string token]
        {
            set => locations[token] = value;
        }

        public bool TryLocate(QuestMarkerTarget target, out QuestTargetLocation location) =>
            locations.TryGetValue(target.ToToken(), out location);
    }
}
