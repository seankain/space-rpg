// The single source of truth for world-map geometry: how big a chunk's baked
// image is and how a world position lands on it. Both the editor bake tool
// (MapBaker) and the runtime map UI go through here, so the two can never
// disagree about where a pixel is.
//
// Orientation: baked images look straight down with world -Z at the top
// ("north") and +X to the right, so a world position maps to pixels with no
// axis flip — X grows rightward, Z grows downward.
//
// Engine-free on purpose (no Godot types): the xunit project compiles this
// file directly.
public static class MapProjection
{
    // Mirrors the chunk grid (see ChunkManager, which aliases this constant):
    // chunk (x, z) is centered on world (x·64, 0, z·64), content in [-32, 32).
    public const float ChunkSize = 64f;

    // Baked image density. 4 px per world unit puts one 64×64 chunk on a
    // 256×256 image — sharp enough to read structures, small enough to commit.
    public const int PixelsPerUnit = 4;

    public const int ChunkPixels = (int)ChunkSize * PixelsPerUnit;

    // Chunk-local position (as authored inside Chunk_<x>_<z>.tscn) to world.
    public static float ChunkToWorldX(int chunkX, float localX) =>
        chunkX * ChunkSize + localX;

    public static float ChunkToWorldZ(int chunkZ, float localZ) =>
        chunkZ * ChunkSize + localZ;

    // World position to pixels within a stitched area image whose top-left
    // chunk is (minChunkX, minChunkZ). The stitched image's origin is that
    // chunk's top-left corner: world (minChunkX·64 − 32, minChunkZ·64 − 32).
    public static float WorldToPixelX(float worldX, int minChunkX) =>
        (worldX - (minChunkX * ChunkSize - ChunkSize / 2f)) * PixelsPerUnit;

    public static float WorldToPixelY(float worldZ, int minChunkZ) =>
        (worldZ - (minChunkZ * ChunkSize - ChunkSize / 2f)) * PixelsPerUnit;
}
