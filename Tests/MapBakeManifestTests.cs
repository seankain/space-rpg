using Xunit;

public class MapBakeManifestTests
{
    [Fact]
    public void RoundTripsThroughJson()
    {
        var manifest = new MapBakeManifest();
        manifest.ChunkHashes["Chunk_0_0.tscn"] = "abc123";
        manifest.ChunkHashes["Chunk_1_0.tscn"] = "def456";

        var loaded = MapBakeManifest.FromJson(manifest.ToJson());

        Assert.Equal(MapBakeManifest.CurrentVersion, loaded.Version);
        Assert.Equal(2, loaded.ChunkHashes.Count);
        Assert.Equal("abc123", loaded.ChunkHashes["Chunk_0_0.tscn"]);
        Assert.Equal("def456", loaded.ChunkHashes["Chunk_1_0.tscn"]);
    }

    [Fact]
    public void ToleratesSparseJson()
    {
        var loaded = MapBakeManifest.FromJson("{}");
        Assert.Empty(loaded.ChunkHashes);
    }

    [Fact]
    public void HashIsStableAndSensitiveToContent()
    {
        var a = MapBakeManifest.HashSceneText("[node name=\"Floor\"]\n");
        var again = MapBakeManifest.HashSceneText("[node name=\"Floor\"]\n");
        var changed = MapBakeManifest.HashSceneText("[node name=\"Wall\"]\n");

        Assert.Equal(a, again);
        Assert.NotEqual(a, changed);
    }

    [Fact]
    public void HashIgnoresLineEndingStyle()
    {
        // A CRLF checkout must hash the same as an LF one, or every Windows
        // clone would read as stale.
        Assert.Equal(
            MapBakeManifest.HashSceneText("line one\nline two\n"),
            MapBakeManifest.HashSceneText("line one\r\nline two\r\n"));
    }
}
