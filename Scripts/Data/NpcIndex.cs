using System;
using System.Collections.Generic;

// Engine-free view of an NPC definition so the index/validation logic below
// (and its tests) compile without Godot. NpcDefinition is the Godot-facing
// implementor; chunk coordinates are flattened to ints for the same reason.
public interface INpcDefinition
{
    string NpcId { get; }
    string DisplayName { get; }
    string SpawnScenePath { get; }
    int ChunkX { get; }
    int ChunkZ { get; }
    IEnumerable<uint> ItemIds { get; }
}

// The validation and dual-index core of NpcDatabase: definitions are indexed
// by NpcId (uniqueness is enforced here) and by spawn scene, with chunk
// filtering on top. Engine-free like PartyManager so the Tests project can
// exercise it directly; NpcDatabase owns the Godot side (directory scan,
// resource loading, GD.PushError).
public class NpcIndex<TDef> where TDef : class, INpcDefinition
{
    private readonly Dictionary<string, TDef> byId = new();
    private readonly Dictionary<string, List<TDef>> byScene = new();
    private readonly Func<uint, bool> itemExists;
    private readonly Action<string> reportError;

    public NpcIndex(Func<uint, bool> itemExists, Action<string> reportError)
    {
        this.itemExists = itemExists ?? (_ => true);
        this.reportError = reportError ?? (_ => {});
    }

    public IReadOnlyCollection<TDef> All => byId.Values;

    // Validates and indexes one definition; source names the offending file
    // in errors. A definition that fails any check is skipped entirely,
    // never partially indexed.
    public bool TryAdd(string source, TDef def)
    {
        if (def == null)
        {
            reportError($"'{source}' is not an NPC definition.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(def.NpcId))
        {
            reportError($"'{source}' has an empty NpcId.");
            return false;
        }
        if (byId.ContainsKey(def.NpcId))
        {
            reportError($"'{source}' duplicates NpcId '{def.NpcId}'; NPC ids must be unique.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(def.SpawnScenePath))
        {
            reportError($"'{source}' ('{def.NpcId}') has no SpawnScenePath.");
            return false;
        }
        foreach (var itemId in def.ItemIds)
        {
            if (!itemExists(itemId))
            {
                reportError($"'{source}' ('{def.NpcId}') lists unknown item id {itemId}.");
                return false;
            }
        }
        byId.Add(def.NpcId, def);
        if (!byScene.TryGetValue(def.SpawnScenePath, out var inScene))
        {
            byScene[def.SpawnScenePath] = inScene = new List<TDef>();
        }
        inScene.Add(def);
        return true;
    }

    public TDef Get(string npcId) =>
        npcId != null && byId.TryGetValue(npcId, out var def) ? def : null;

    // Every NPC hosted by a scene, chunked or not (interiors ignore chunks).
    public IReadOnlyList<TDef> InScene(string scenePath) =>
        scenePath != null && byScene.TryGetValue(scenePath, out var defs) ? defs : Array.Empty<TDef>();

    // NPCs of one chunk of a chunked level.
    public IReadOnlyList<TDef> AtChunk(string scenePath, int chunkX, int chunkZ)
    {
        var matches = new List<TDef>();
        foreach (var def in InScene(scenePath))
        {
            if (def.ChunkX == chunkX && def.ChunkZ == chunkZ)
            {
                matches.Add(def);
            }
        }
        return matches;
    }

    // Pre-v7 saves keyed DefeatedNpcs by display name; this is the lookup
    // that migration uses to rewrite those entries to stable ids.
    public TDef FindByDisplayName(string displayName)
    {
        if (string.IsNullOrEmpty(displayName))
        {
            return null;
        }
        foreach (var def in byId.Values)
        {
            if (def.DisplayName == displayName)
            {
                return def;
            }
        }
        return null;
    }
}
