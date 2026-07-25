using Godot;
using System.Collections.Generic;

// Streams hand-authored level chunks in and out around the player. There is
// no procedural generation: every chunk is a scene file in ChunkDirectory
// named Chunk_<x>_<z>.tscn, where (x, z) is its grid coordinate. Chunk (x, z)
// is centered on world position (x * 64, 0, z * 64), so chunk content is
// authored in local coordinates within [-32, 32) on the X and Z axes and
// adjacent chunks tile seamlessly.
public partial class ChunkManager : Node3D
{
    // Aliased from MapProjection so the streaming grid and the world-map
    // bake/UI can never disagree about chunk dimensions.
    public const float ChunkSize = MapProjection.ChunkSize;

    [Export]
    public string ChunkDirectory = "res://Scenes/Levels/Chunks/IntroStation";

    // Chunks within LoadRadius grid steps (Chebyshev distance) of the
    // player's chunk are loaded; loaded chunks farther than UnloadRadius are
    // freed. The gap between the two keeps a chunk from load/unload thrashing
    // while the player walks along a border.
    [Export]
    public int LoadRadius = 1;

    [Export]
    public int UnloadRadius = 2;

    private readonly Dictionary<Vector2I, string> available = new();
    private readonly Dictionary<Vector2I, Node3D> loaded = new();
    private readonly List<Vector2I> pending = new();

    public static Vector2I ToChunkCoord(Vector3 worldPosition) => new(
        Mathf.RoundToInt(worldPosition.X / ChunkSize),
        Mathf.RoundToInt(worldPosition.Z / ChunkSize));

    // The loaded chunk node at a coordinate, or null if it isn't streamed in.
    // The in-game editor parents a placement preview into the live chunk so it
    // runs like any streamed NPC (in-game-editor plan Phase 3).
    public Node3D GetLoadedChunk(Vector2I coord) => loaded.GetValueOrDefault(coord);

    public override void _Ready()
    {
        foreach (var (coord, path) in DiscoverChunks(ChunkDirectory))
        {
            available[coord] = path;
        }
        // The player is added by Level after this node readies, so the
        // starting neighborhood is loaded synchronously — the ground has to
        // exist before the player's first physics frame.
        foreach (var coord in CoordsAround(ToChunkCoord(InitialFocus()), LoadRadius))
        {
            if (available.TryGetValue(coord, out var path) && !loaded.ContainsKey(coord))
            {
                AddChunk(coord, GD.Load<PackedScene>(path));
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (GetTree().GetFirstNodeInGroup(Player.GroupName) is not Node3D player)
        {
            return;
        }
        var center = ToChunkCoord(player.GlobalPosition);
        foreach (var coord in CoordsAround(center, LoadRadius))
        {
            if (available.ContainsKey(coord) && !loaded.ContainsKey(coord) && !pending.Contains(coord))
            {
                ResourceLoader.LoadThreadedRequest(available[coord]);
                pending.Add(coord);
            }
        }
        PollPendingLoads();
        UnloadFarChunks(center);
    }

    // The chunk grid is discovered from the file names in a chunk directory,
    // so authoring a new chunk is just saving Chunk_<x>_<z>.tscn there — no
    // manifest to keep in sync. Static so the editor map baker walks the same
    // grid the game streams.
    public static Dictionary<Vector2I, string> DiscoverChunks(string chunkDirectory)
    {
        var chunks = new Dictionary<Vector2I, string>();
        using var dir = DirAccess.Open(chunkDirectory);
        if (dir == null)
        {
            GD.PushError($"Chunk directory '{chunkDirectory}' not found.");
            return chunks;
        }
        foreach (var file in dir.GetFiles())
        {
            // Exported builds list scene files with a .remap suffix.
            var name = file.EndsWith(".remap") ? file[..^".remap".Length] : file;
            if (!name.EndsWith(".tscn"))
            {
                continue;
            }
            var parts = name[..^".tscn".Length].Split('_');
            if (parts.Length != 3 || parts[0] != "Chunk"
                || !int.TryParse(parts[1], out var x) || !int.TryParse(parts[2], out var z))
            {
                GD.PushWarning($"Ignoring '{chunkDirectory}/{name}': chunk scenes must be named Chunk_<x>_<z>.tscn.");
                continue;
            }
            chunks[new Vector2I(x, z)] = $"{chunkDirectory}/{name}";
        }
        return chunks;
    }

    // Where the player is about to appear: the saved position when resuming a
    // save, otherwise the level's Spawn marker (same rule as Level.AddPlayer).
    private Vector3 InitialFocus()
    {
        var saved = SaveManager.Instance?.CurrentState?.PlayerPosition;
        if (saved != null)
        {
            return SaveManager.ToGodot(saved.Value);
        }
        foreach (var c in GetParent().GetChildren())
        {
            if (c is Spawn spawn)
            {
                return spawn.GlobalPosition;
            }
        }
        return Vector3.Zero;
    }

    private static IEnumerable<Vector2I> CoordsAround(Vector2I center, int radius)
    {
        for (var x = center.X - radius; x <= center.X + radius; x++)
        {
            for (var z = center.Y - radius; z <= center.Y + radius; z++)
            {
                yield return new Vector2I(x, z);
            }
        }
    }

    private void PollPendingLoads()
    {
        for (var i = pending.Count - 1; i >= 0; i--)
        {
            var coord = pending[i];
            var path = available[coord];
            switch (ResourceLoader.LoadThreadedGetStatus(path))
            {
                case ResourceLoader.ThreadLoadStatus.Loaded:
                    pending.RemoveAt(i);
                    AddChunk(coord, (PackedScene)ResourceLoader.LoadThreadedGet(path));
                    break;
                case ResourceLoader.ThreadLoadStatus.InProgress:
                    break;
                default:
                    pending.RemoveAt(i);
                    available.Remove(coord);
                    GD.PushError($"Failed to load chunk '{path}'.");
                    break;
            }
        }
    }

    private void AddChunk(Vector2I coord, PackedScene scene)
    {
        var chunk = (Node3D)scene.Instantiate();
        chunk.Position = new Vector3(coord.X * ChunkSize, 0f, coord.Y * ChunkSize);
        // NPCs are authored as NpcDefinition resources, not chunk-scene
        // nodes. Spawning them as chunk children keeps their old lifecycle:
        // freed with the chunk on unload, _Ready state checks (defeated,
        // recruited) re-run when it streams back in.
        foreach (var definition in NpcDatabase.ForChunk(LevelScenePath(), coord))
        {
            NpcSpawner.Spawn(definition, chunk);
        }
        AddChild(chunk);
        loaded[coord] = chunk;
    }

    // The hosting level scene (e.g. Intro.tscn) — the path NpcDefinitions
    // name in SpawnScenePath.
    private string LevelScenePath() =>
        Owner?.SceneFilePath ?? GetParent()?.SceneFilePath ?? "";

    private void UnloadFarChunks(Vector2I center)
    {
        List<Vector2I> far = null;
        foreach (var coord in loaded.Keys)
        {
            if (Mathf.Max(Mathf.Abs(coord.X - center.X), Mathf.Abs(coord.Y - center.Y)) > UnloadRadius)
            {
                (far ??= new()).Add(coord);
            }
        }
        if (far == null)
        {
            return;
        }
        foreach (var coord in far)
        {
            loaded[coord].QueueFree();
            loaded.Remove(coord);
        }
    }
}
