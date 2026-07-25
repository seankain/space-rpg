using Xunit;

// In-game-editor plan Phase 3 acceptance for the engine-free placement math:
// splitting a world position into an NpcDefinition's chunk coordinate + local
// offset, and deriving the area folder from a level scene path. The Godot-facing
// EditorPlacement (ResourceSaver, NpcSpawner) is exercised in the running editor.
public class NpcPlacementTests
{
    private const float ChunkSize = MapProjection.ChunkSize; // 64

    [Theory]
    [InlineData(0f, 0f, 0f)]
    [InlineData(10f, 1.5f, -7f)]
    [InlineData(33f, 0f, -33f)]     // just across the +/- borders
    [InlineData(95f, 2f, -95f)]
    [InlineData(-40f, 0f, 40f)]
    [InlineData(200.25f, 5f, -137.75f)]
    public void SplitWorldRoundTripsToOriginalPosition(float x, float y, float z)
    {
        var split = PlacementMath.SplitWorld(x, y, z);
        // Reassembling world = chunk*size + local must recover the input exactly.
        Assert.Equal(x, split.ChunkX * ChunkSize + split.LocalX, 3);
        Assert.Equal(z, split.ChunkZ * ChunkSize + split.LocalZ, 3);
        // Y is not chunked; it passes straight through.
        Assert.Equal(y, split.LocalY, 3);
    }

    [Theory]
    [InlineData(10f, 0)]
    [InlineData(31f, 0)]
    [InlineData(33f, 1)]
    [InlineData(40f, 1)]
    [InlineData(95f, 1)]
    [InlineData(100f, 2)]
    [InlineData(-33f, -1)]
    [InlineData(-95f, -1)]
    [InlineData(-100f, -2)]
    public void ToChunkCoordMatchesTheStreamingGrid(float world, int expectedChunk)
    {
        Assert.Equal(expectedChunk, PlacementMath.ToChunkCoord(world));
    }

    [Theory]
    [InlineData(10f)]
    [InlineData(33f)]
    [InlineData(-33f)]
    [InlineData(95f)]
    [InlineData(-95f)]
    [InlineData(150f)]
    public void LocalOffsetStaysWithinHalfAChunk(float world)
    {
        var split = PlacementMath.SplitWorld(world, 0f, world);
        // Chunk-local content is authored in [-32, 32).
        Assert.InRange(split.LocalX, -ChunkSize / 2f, ChunkSize / 2f);
        Assert.InRange(split.LocalZ, -ChunkSize / 2f, ChunkSize / 2f);
    }

    [Theory]
    [InlineData("res://Scenes/Levels/Intro.tscn", "Intro")]
    [InlineData("res://Scenes/Levels/ShopInterior.tscn", "ShopInterior")]
    [InlineData("Chunk_0_0.tscn", "Chunk_0_0")]
    [InlineData("res://Scenes/Levels/NoExtension", "NoExtension")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void AreaFromScenePathTakesTheBasename(string scenePath, string expected)
    {
        Assert.Equal(expected, PlacementMath.AreaFromScenePath(scenePath));
    }
}
