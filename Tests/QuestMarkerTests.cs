using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

// quest-markers.md Phase 1: the authored marker data, the conditions that
// decide which of them apply, and the resolver that hands the map a position.
// Engine-free throughout — target positions arrive through a fake locator,
// standing in for the NpcDatabase/map-bake lookups of Phase 2.
public class QuestMarkerTests
{
    private const uint MaguffinCube = ItemCatalog.MaguffinCubeId;

    [Fact]
    public void FetchQuestPointsAtTheCubeUntilThePartyHasIt()
    {
        var state = InProgress(QuestCatalog.ReturnTheMaguffinId);

        var before = QuestMarkerResolver.ActiveMarkers(state, QuestCatalog.ReturnTheMaguffinId);

        var marker = Assert.Single(before);
        Assert.Equal(QuestMarkerTargetKind.Item, marker.Target.Kind);
        Assert.True(marker.Target.TryItemId(out var itemId));
        Assert.Equal(MaguffinCube, itemId);
        Assert.Equal("Find the Maguffin Cube", marker.Label);
    }

    [Fact]
    public void FetchQuestPointsAtHaleOnceTheCubeIsInTheInventory()
    {
        var state = InProgress(QuestCatalog.ReturnTheMaguffinId);
        state.Inventory.Add(MaguffinCube);

        var after = QuestMarkerResolver.ActiveMarkers(state, QuestCatalog.ReturnTheMaguffinId);

        var marker = Assert.Single(after);
        Assert.Equal(QuestMarkerTargetKind.Npc, marker.Target.Kind);
        Assert.Equal("intro.dockmaster_hale", marker.Target.Reference);
    }

    [Fact]
    public void BountyQuestPointsAtVexThenMarlow()
    {
        var state = InProgress(QuestCatalog.ClearTheDeckId);

        Assert.Equal(
            "intro.vex",
            Assert.Single(QuestMarkerResolver.ActiveMarkers(state, QuestCatalog.ClearTheDeckId)).Target.Reference);

        state.MarkNpcDefeated("intro.vex");

        Assert.Equal(
            "intro.chief_marlow",
            Assert.Single(QuestMarkerResolver.ActiveMarkers(state, QuestCatalog.ClearTheDeckId)).Target.Reference);
    }

    [Theory]
    [InlineData(QUESTSUCCESSSTATE.Unstarted)]
    [InlineData(QUESTSUCCESSSTATE.Success)]
    [InlineData(QUESTSUCCESSSTATE.Failed)]
    public void OnlyAnInProgressQuestHasMarkers(QUESTSUCCESSSTATE questState)
    {
        var state = new GameState();
        state.SetQuestState(QuestCatalog.ReturnTheMaguffinId, questState);

        Assert.Empty(QuestMarkerResolver.ActiveMarkers(state, QuestCatalog.ReturnTheMaguffinId));
    }

    [Fact]
    public void NoStateOrUnknownQuestResolvesToNothing()
    {
        Assert.Empty(QuestMarkerResolver.ActiveMarkers(null, QuestCatalog.ReturnTheMaguffinId));
        Assert.Empty(QuestMarkerResolver.ActiveMarkers(InProgress(9999), 9999));
    }

    [Fact]
    public void ResolveAttachesPositionsAndQuestMetadata()
    {
        var state = InProgress(QuestCatalog.ClearTheDeckId);
        var locator = new FakeLocator
        {
            ["npc:intro.vex"] = new QuestTargetLocation
            {
                AreaName = "IntroStation",
                ScenePath = "res://Scenes/Levels/Intro.tscn",
                WorldX = 12f,
                WorldZ = -4f,
            },
        };

        var resolved = Assert.Single(QuestMarkerResolver.Resolve(state, QuestCatalog.ClearTheDeckId, locator));

        Assert.Equal(QuestCatalog.ClearTheDeckId, resolved.QuestId);
        Assert.True(resolved.SideQuest);
        Assert.Equal("Deal with Vex", resolved.Label);
        Assert.Equal("IntroStation", resolved.Location.AreaName);
        Assert.Equal(12f, resolved.Location.WorldX);
        Assert.Equal(-4f, resolved.Location.WorldZ);
    }

    [Fact]
    public void ResolveDropsTargetsTheLocatorCannotPlace()
    {
        var state = InProgress(QuestCatalog.ClearTheDeckId);
        var warnings = new List<string>();

        // An empty locator stands for "that NPC isn't in this area": the map
        // shows nothing for it rather than erroring.
        var resolved = QuestMarkerResolver.Resolve(
            state, QuestCatalog.ClearTheDeckId, new FakeLocator(), warnings.Add);

        Assert.Empty(resolved);
        Assert.Contains(warnings, w => w.Contains("npc:intro.vex"));
    }

    [Fact]
    public void ResolveWithoutALocatorIsEmptyRatherThanThrowing()
    {
        var state = InProgress(QuestCatalog.ReturnTheMaguffinId);

        Assert.Empty(QuestMarkerResolver.Resolve(state, QuestCatalog.ReturnTheMaguffinId, null));
    }

    [Fact]
    public void EveryShippedMarkerValidates()
    {
        // The catalog validates itself at registration; this states the same
        // guarantee as a test, so a bad marker names itself in a failure
        // message instead of only blowing up the type initializer.
        foreach (var quest in QuestCatalog.All)
        {
            foreach (var marker in quest.Markers)
            {
                Assert.Null(QuestMarker.Validate(marker));
            }
        }
    }

    [Fact]
    public void EveryShippedMarkerLabelsItselfDistinctlyWithinItsQuest()
    {
        foreach (var quest in QuestCatalog.All)
        {
            var labels = quest.Markers.Select(m => m.Label).ToList();
            Assert.Equal(labels.Count, labels.Distinct().Count());
        }
    }

    [Fact]
    public void ValidateRejectsBrokenMarkers()
    {
        Assert.NotNull(QuestMarker.Validate(null));
        Assert.NotNull(QuestMarker.Validate(new QuestMarker { Label = "No target" }));
        Assert.NotNull(QuestMarker.Validate(QuestMarker.Create(QuestMarkerTarget.Npc(""), "Empty id")));
        Assert.NotNull(QuestMarker.Validate(QuestMarker.Create(QuestMarkerTarget.Npc("intro.vex"), "  ")));
        // An item target has a catalog to check against, unlike an NPC id.
        Assert.NotNull(QuestMarker.Validate(QuestMarker.Create(QuestMarkerTarget.Item(9999), "Ghost item")));
        // The condition goes through the dialogue vocabulary's own validator.
        Assert.NotNull(QuestMarker.Validate(
            QuestMarker.Create(QuestMarkerTarget.Npc("intro.vex"), "Bad gate", "no_such_condition:1")));
        Assert.NotNull(QuestMarker.Validate(
            QuestMarker.Create(QuestMarkerTarget.Npc("intro.vex"), "Bad arg", "has_item:nope")));
        // A colon would split the reference in two on the way through a token.
        Assert.NotNull(QuestMarker.Validate(
            QuestMarker.Create(QuestMarkerTarget.Npc("intro:vex"), "Colon in id")));
    }

    [Fact]
    public void ValidateAcceptsAnUngatedMarker()
    {
        Assert.Null(QuestMarker.Validate(QuestMarker.Create(QuestMarkerTarget.Npc("intro.rig"), "Find Rig")));
    }

    [Fact]
    public void AMarkerWithAnUnevaluableConditionIsSkippedRatherThanShown()
    {
        // Content the catalog would have rejected, reaching the resolver from a
        // hand-edited or future data-authored definition: it hides the marker
        // and says why, the fail-closed rule a conversation's branch gates
        // follow.
        var quest = new Quest
        {
            Id = 4242,
            Title = "Broken",
            Markers =
            {
                QuestMarker.Create(QuestMarkerTarget.Npc("intro.vex"), "Bad", "no_such_condition"),
                QuestMarker.Create(QuestMarkerTarget.Npc("intro.rig"), "Good"),
            },
        };
        var state = InProgress(quest.Id);
        var warnings = new List<string>();

        var active = QuestMarkerResolver.ActiveMarkers(state, quest, warnings.Add);

        Assert.Equal("Good", Assert.Single(active).Label);
        Assert.Contains(warnings, w => w.Contains("no_such_condition"));
    }

    [Fact]
    public void AGatedMarkerAppearsOnlyWhenItsConditionHolds()
    {
        // The gate is read against live state on every call — no caching, so a
        // marker follows an inventory change without anything being rebuilt.
        var quest = new Quest
        {
            Id = 4243,
            Title = "Gated",
            Markers = { QuestMarker.Create(QuestMarkerTarget.Npc("intro.rig"), "Bring the cube", $"has_item:{MaguffinCube}") },
        };
        var state = InProgress(quest.Id);

        Assert.Empty(QuestMarkerResolver.ActiveMarkers(state, quest));

        state.Inventory.Add(MaguffinCube);

        Assert.Single(QuestMarkerResolver.ActiveMarkers(state, quest));
    }

    [Theory]
    [InlineData("npc:intro.vex")]
    [InlineData("item:1")]
    [InlineData("point:IntroStation:64:-12.5")]
    public void TargetTokensRoundTrip(string token)
    {
        var parsed = QuestMarkerTarget.Parse(token);

        Assert.NotNull(parsed);
        Assert.Equal(token, parsed.ToToken());
    }

    [Fact]
    public void PointTokenCarriesItsCoordinates()
    {
        var parsed = QuestMarkerTarget.Parse("point:IntroStation:64:-12.5");

        Assert.Equal(QuestMarkerTargetKind.Point, parsed.Kind);
        Assert.Equal("IntroStation", parsed.Reference);
        Assert.Equal(64f, parsed.X);
        Assert.Equal(-12.5f, parsed.Z);
    }

    [Theory]
    [InlineData("")]
    [InlineData("npc")]
    [InlineData("nope:intro.vex")]
    [InlineData("npc:intro.vex:extra")]
    [InlineData("point:IntroStation:64")]
    [InlineData("point:IntroStation:64:north")]
    public void MalformedTargetTokensParseToNothing(string token)
    {
        Assert.Null(QuestMarkerTarget.Parse(token));
    }

    private static GameState InProgress(uint questId)
    {
        var state = new GameState();
        state.SetQuestState(questId, QUESTSUCCESSSTATE.InProgress);
        return state;
    }

    // Positions by target token — what Phase 2's NpcDatabase/map-bake locator
    // will answer for real.
    private sealed class FakeLocator : IQuestTargetLocator
    {
        private readonly Dictionary<string, QuestTargetLocation> locations = new(StringComparer.Ordinal);

        public QuestTargetLocation this[string token]
        {
            set => locations[token] = value;
        }

        public bool TryLocate(QuestMarkerTarget target, out QuestTargetLocation location) =>
            locations.TryGetValue(target.ToToken(), out location);
    }
}
