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
}
