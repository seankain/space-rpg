using System.Linq;
using Xunit;

public class MapLandmarkTests
{
    [Fact]
    public void RoundTripsThroughJson()
    {
        var file = new MapLandmarksFile();
        file.Landmarks.Add(new MapLandmark
        {
            Type = MapLandmark.PortalType,
            Name = "World 1",
            X = 12.5f,
            Z = -3.25f,
        });
        file.Landmarks.Add(new MapLandmark
        {
            Type = MapLandmark.DoorType,
            Name = "Shop",
            X = -70f,
            Z = 64f,
        });

        var loaded = MapLandmarksFile.FromJson(file.ToJson());

        Assert.Equal(MapLandmarksFile.CurrentVersion, loaded.Version);
        Assert.Equal(2, loaded.Landmarks.Count);
        Assert.Equal(MapLandmark.PortalType, loaded.Landmarks[0].Type);
        Assert.Equal("World 1", loaded.Landmarks[0].Name);
        Assert.Equal(12.5f, loaded.Landmarks[0].X);
        Assert.Equal(-3.25f, loaded.Landmarks[0].Z);
        Assert.Equal(MapLandmark.DoorType, loaded.Landmarks[1].Type);
    }

    [Fact]
    public void ToleratesSparseJson()
    {
        // A hand-edited or future-version manifest without landmarks still
        // loads as an empty, usable file.
        var loaded = MapLandmarksFile.FromJson("{}");
        Assert.Empty(loaded.Landmarks);

        loaded = MapLandmarksFile.FromJson("{\"Version\": 1, \"Landmarks\": null}");
        Assert.Empty(loaded.Landmarks);
    }

    [Fact]
    public void RoundTripsPickupsAndDoorDestinations()
    {
        // quest-markers.md Phase 2: what a quest marker points at — where an
        // item is lying, and where a door leads.
        var file = new MapLandmarksFile();
        file.Landmarks.Add(new MapLandmark
        {
            Type = MapLandmark.PickupType,
            Name = "Maguffin Cube",
            ItemId = 1,
            X = 4f,
            Z = -8f,
        });
        file.Landmarks.Add(new MapLandmark
        {
            Type = MapLandmark.DoorType,
            Name = "Supply Shop",
            TargetScenePath = "res://Scenes/Levels/ShopInterior.tscn",
            X = 20f,
            Z = 2f,
        });

        var loaded = MapLandmarksFile.FromJson(file.ToJson());

        var pickup = loaded.FindPickup(1);
        Assert.NotNull(pickup);
        Assert.Equal("Maguffin Cube", pickup.Name);
        Assert.Equal(4f, pickup.X);
        Assert.Equal(-8f, pickup.Z);
        Assert.Null(loaded.FindPickup(2));

        var door = loaded.FindEntranceTo("res://Scenes/Levels/ShopInterior.tscn");
        Assert.NotNull(door);
        Assert.Equal("Supply Shop", door.Name);
        Assert.Null(loaded.FindEntranceTo("res://Scenes/Levels/Nowhere.tscn"));
        Assert.Null(loaded.FindEntranceTo(""));
    }

    [Fact]
    public void OfTypeSelectsOneKind()
    {
        var file = new MapLandmarksFile();
        file.Landmarks.Add(new MapLandmark { Type = MapLandmark.PortalType, Name = "World 1" });
        file.Landmarks.Add(new MapLandmark { Type = MapLandmark.PickupType, ItemId = 1 });
        file.Landmarks.Add(new MapLandmark { Type = MapLandmark.PickupType, ItemId = 5 });

        Assert.Equal(2, file.OfType(MapLandmark.PickupType).Count());
        Assert.Single(file.OfType(MapLandmark.PortalType));
        Assert.Empty(file.OfType(MapLandmark.DoorType));
    }

    [Fact]
    public void AV1ManifestStillLoads()
    {
        // Areas baked before pickups were recorded: every entry reads back with
        // the new fields defaulted, so item markers find nothing until the area
        // is re-baked rather than the map failing to load at all.
        var loaded = MapLandmarksFile.FromJson(
            "{\"Version\": 1, \"Landmarks\": [{\"Type\": \"portal\", \"Name\": \"World 1\", \"X\": 1, \"Z\": 2}]}");

        var landmark = Assert.Single(loaded.Landmarks);
        Assert.Equal(MapLandmark.PortalType, landmark.Type);
        Assert.Equal(0u, landmark.ItemId);
        Assert.Equal("", landmark.TargetScenePath);
        Assert.Null(loaded.FindPickup(1));
    }
}
