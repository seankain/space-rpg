using Xunit;

public class MapProjectionTests
{
    [Fact]
    public void ChunkPixelsCoversOneChunkAtBakeDensity()
    {
        Assert.Equal(256, MapProjection.ChunkPixels);
    }

    [Theory]
    [InlineData(0, 0f, 0f)]
    [InlineData(1, 5f, 69f)]
    [InlineData(-1, -5f, -69f)]
    [InlineData(2, -31.5f, 96.5f)]
    public void ChunkToWorldOffsetsByWholeChunks(int chunk, float local, float expected)
    {
        Assert.Equal(expected, MapProjection.ChunkToWorldX(chunk, local));
        Assert.Equal(expected, MapProjection.ChunkToWorldZ(chunk, local));
    }

    [Fact]
    public void TopLeftCornerOfMinChunkIsPixelOrigin()
    {
        // Chunk (0, 0)'s content spans [-32, 32); its top-left corner is the
        // stitched image's origin when it is the minimum chunk.
        Assert.Equal(0f, MapProjection.WorldToPixelX(-32f, 0));
        Assert.Equal(0f, MapProjection.WorldToPixelY(-32f, 0));
    }

    [Fact]
    public void ChunkCenterMapsToImageCenter()
    {
        Assert.Equal(128f, MapProjection.WorldToPixelX(0f, 0));
        Assert.Equal(128f, MapProjection.WorldToPixelY(0f, 0));
    }

    [Fact]
    public void NegativeMinChunkShiftsTheOrigin()
    {
        // With the grid starting at chunk (-1, -1), that chunk's top-left
        // corner (world -96) becomes pixel 0 and chunk (0, 0)'s center lands
        // one chunk-image further in.
        Assert.Equal(0f, MapProjection.WorldToPixelX(-96f, -1));
        Assert.Equal(256f + 128f, MapProjection.WorldToPixelX(0f, -1));
    }

    [Fact]
    public void NeighboringChunksTileSeamlessly()
    {
        // The seam between chunk 0 and chunk 1 (world x = 32) is exactly one
        // chunk image in.
        Assert.Equal(256f, MapProjection.WorldToPixelX(32f, 0));
    }
}
