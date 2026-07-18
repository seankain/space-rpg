using Godot;
using System.Collections.Generic;

// All NPC definitions, loaded once on first use (ItemCatalog's
// catalog-by-id role, but data-driven and editable in the inspector).
// Discovery is a recursive scan of Resources/Npcs — authoring an NPC is just
// saving a .tres there, no manifest to drift out of sync (same rationale as
// ChunkManager.DiscoverChunks). Definitions are shared, cached resources:
// treat them as immutable at runtime.
public static class NpcDatabase
{
    public const string NpcDirectory = "res://Resources/Npcs";

    private static NpcIndex<NpcDefinition> index;

    private static NpcIndex<NpcDefinition> Index
    {
        get
        {
            if (index == null)
            {
                index = new NpcIndex<NpcDefinition>(
                    itemId => ItemCatalog.Get(itemId) != null,
                    error => GD.PushError(error));
                LoadDirectory(NpcDirectory);
            }
            return index;
        }
    }

    public static IReadOnlyCollection<NpcDefinition> All => Index.All;

    public static NpcDefinition Get(string npcId) => Index.Get(npcId);

    // NPCs of one chunk of a chunked level; ChunkManager spawns these as
    // children of the chunk node.
    public static IReadOnlyList<NpcDefinition> ForChunk(string scenePath, Vector2I chunkCoords) =>
        Index.AtChunk(scenePath, chunkCoords.X, chunkCoords.Y);

    // Every NPC of a non-chunked scene (interiors); NpcSpawner spawns these
    // on scene ready. Chunk coordinates are ignored.
    public static IReadOnlyList<NpcDefinition> ForScene(string scenePath) =>
        Index.InScene(scenePath);

    // Pre-v7 saves keyed GameState.DefeatedNpcs by display name; rewrite any
    // entry matching a known NPC's display name to its stable id. Entries
    // that match nothing (or whose id is already recorded) are left alone.
    public static void MigrateLegacyDefeatedNames(GameState state)
    {
        if (state?.DefeatedNpcs == null)
        {
            return;
        }
        for (var i = 0; i < state.DefeatedNpcs.Count; i++)
        {
            if (Get(state.DefeatedNpcs[i]) == null
                && Index.FindByDisplayName(state.DefeatedNpcs[i]) is { } def
                && !state.DefeatedNpcs.Contains(def.NpcId))
            {
                state.DefeatedNpcs[i] = def.NpcId;
            }
        }
    }

    private static void LoadDirectory(string directoryPath)
    {
        using var dir = DirAccess.Open(directoryPath);
        if (dir == null)
        {
            GD.PushError($"NPC definition directory '{directoryPath}' not found.");
            return;
        }
        foreach (var subdirectory in dir.GetDirectories())
        {
            LoadDirectory($"{directoryPath}/{subdirectory}");
        }
        foreach (var file in dir.GetFiles())
        {
            // Exported builds list resource files with a .remap suffix.
            var name = file.EndsWith(".remap") ? file[..^".remap".Length] : file;
            if (!name.EndsWith(".tres"))
            {
                continue;
            }
            var path = $"{directoryPath}/{name}";
            // A non-NpcDefinition .tres arrives here as null and is reported
            // by TryAdd's null check.
            index.TryAdd(path, ResourceLoader.Load(path) as NpcDefinition);
        }
    }
}
