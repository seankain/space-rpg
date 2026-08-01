using System;
using System.Collections.Generic;
using System.Globalization;

// A world position split into the chunk that owns it plus the chunk-local
// offset authored inside that chunk (in-game-editor plan Phase 3).
public readonly struct ChunkPlacement
{
    public ChunkPlacement(int chunkX, int chunkZ, float localX, float localY, float localZ)
    {
        ChunkX = chunkX;
        ChunkZ = chunkZ;
        LocalX = localX;
        LocalY = localY;
        LocalZ = localZ;
    }

    public int ChunkX { get; }
    public int ChunkZ { get; }
    public float LocalX { get; }
    public float LocalY { get; }
    public float LocalZ { get; }
}

// Engine-free math shared by the placement console commands and their tests:
// turning a world position into an NpcDefinition's ChunkCoords + LocalPosition
// (mirroring ChunkManager.ToChunkCoord and the [-32, 32) local convention), and
// deriving the Resources/Npcs/<Area> folder from a level scene path.
public static class PlacementMath
{
    // Which chunk a world coordinate falls in. Rounds halves away from zero to
    // match Godot's Mathf.RoundToInt (and therefore ChunkManager), so a
    // position exactly on a chunk border lands in the same chunk the game
    // streams it into and its local offset stays within [-32, 32).
    public static int ToChunkCoord(float world) =>
        (int)Math.Round(world / MapProjection.ChunkSize, MidpointRounding.AwayFromZero);

    public static ChunkPlacement SplitWorld(float worldX, float worldY, float worldZ)
    {
        var chunkX = ToChunkCoord(worldX);
        var chunkZ = ToChunkCoord(worldZ);
        // Y has no chunking; it passes straight through as the local height.
        return new ChunkPlacement(
            chunkX,
            chunkZ,
            worldX - chunkX * MapProjection.ChunkSize,
            worldY,
            worldZ - chunkZ * MapProjection.ChunkSize);
    }

    // A free id for an NPC placed by clicking rather than typed at the console
    // (`<area>.npc1`, `<area>.npc2`, …), lower-cased so it matches the
    // committed `intro.vex` style. `taken` is the ids already in the database;
    // the first unused number wins, so deleting one hands its number back.
    public static string NextNpcId(string area, IEnumerable<string> taken)
    {
        var prefix = string.IsNullOrEmpty(area) ? "npc" : area.ToLowerInvariant() + ".npc";
        var used = new HashSet<string>(
            taken ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        for (var n = 1; ; n++)
        {
            var candidate = prefix + n.ToString(CultureInfo.InvariantCulture);
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    // The area folder a placed NPC is saved under: the level scene's basename
    // without directory or extension (res://Scenes/Levels/Intro.tscn -> Intro).
    public static string AreaFromScenePath(string scenePath)
    {
        if (string.IsNullOrEmpty(scenePath))
        {
            return "";
        }
        var slash = scenePath.LastIndexOf('/');
        var name = slash >= 0 ? scenePath[(slash + 1)..] : scenePath;
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }
}
