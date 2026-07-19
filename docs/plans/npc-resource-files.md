# NPC Resource Files — Implementation Plan

Goal: move NPC authoring out of scene files and into Godot resource files (`.tres`), one per NPC, so a single file declares who an NPC is (unique id, name, mesh), where they spawn (scene + chunk coordinates + local position), and what they start with (items and credits). Scenes stop being the source of truth for NPC data; they just host the spawned nodes.

## Where we are

- NPCs are `Scenes/Npc.tscn` instances **embedded directly in chunk scenes** (`Chunk_0_0.tscn` holds Rig, Hale, Vex, and Marlow) and in interiors (`ShopInterior.tscn` holds the shopkeeper), each with a role script override (`RecruitNpc.cs`, `QuestGiverNpc.cs`, …) and per-node exports (`DisplayName`, `BodyColor`).
- **Identity is the display name.** `GameState.DefeatedNpcs`, `EnemyCatalog` encounters, and `BountyGiverNpc.TargetNpcName` all key on `"Vex"` the string — both `GameState` and `EnemyCatalog` carry comments admitting this is a stopgap until NPCs have stable ids.
- **Starting inventory is ad hoc.** `ShopkeeperNpc` declares stock through parallel `int[]` exports (a workaround for Godot's dictionary-export limits); `RecruitNpc` hardcodes its recruit stats and `CharacterId = 2` in C#.
- NPCs have **no mesh pipeline** — every NPC is the placeholder capsule tinted by `BodyColor`. KayKit Adventurers 2.0 (rigged characters + animation library) sits unused in `ThirdParty/`.
- Chunks stream via `ChunkManager` from `Scenes/Levels/Chunks/<Area>/Chunk_<x>_<z>.tscn`, discovered by file name with no manifest. Chunk `(x, z)` is positioned at world `(x·64, 0, z·64)`; NPCs placed inside a chunk are freed with it on unload and respawn on load (their `_Ready` re-checks `GameState`, e.g. `BattleNpc` stays down via `DefeatedNpcs`).
- The project is C# (Godot 4.6), so "resource files" means **custom `Resource` subclasses with `[GlobalClass]`** saved as `.tres` — the same catalog-by-id philosophy as `ItemCatalog`/`QuestCatalog`, but data-driven and editable in the inspector instead of compiled in.

## Design

### The resource schema

Two new resource classes in `Scripts/Data/` (kept there deliberately — `.tres` files reference the C# script *by path*, so these scripts should not move once resources exist):

```csharp
[GlobalClass]
public partial class NpcDefinition : Resource
{
    // Stable unique id, e.g. "intro.vex". The handle quests, saves, and
    // encounters use — permanent once shipped, like item/quest ids.
    [Export] public string NpcId { get; set; } = "";

    [Export] public string DisplayName { get; set; } = "";

    // Where this NPC exists: the hosting scene (a chunked level like
    // Intro.tscn, or an interior like ShopInterior.tscn) …
    [Export(PropertyHint.File, "*.tscn")] public string SpawnScenePath { get; set; } = "";
    // … and, when that scene is chunked, which chunk. Ignored for interiors.
    [Export] public Vector2I ChunkCoords { get; set; }
    // Position within the chunk (chunk-local, [-32, 32)) or within the
    // interior scene. Y rotation in degrees so NPCs face the right way.
    [Export] public Vector3 LocalPosition { get; set; }
    [Export] public float RotationDegreesY { get; set; }

    // Role variant to instantiate: RecruitNpc.tscn, ShopkeeperNpc.tscn, …
    // (thin inherited scenes of Npc.tscn carrying the role script).
    [Export] public PackedScene NpcScene { get; set; }

    // Rigged character scene (KayKit gltf). Null keeps today's capsule,
    // tinted by BodyColor.
    [Export] public PackedScene CharacterMesh { get; set; }
    [Export] public Color BodyColor { get; set; } = Colors.White;

    // Starting wallet and inventory: shop bankroll/stock for shopkeepers,
    // carried items for future recruit/loot use.
    [Export] public uint Credits { get; set; }
    [Export] public NpcItemStack[] InitialItems { get; set; } = System.Array.Empty<NpcItemStack>();
}

[GlobalClass]
public partial class NpcItemStack : Resource
{
    [Export] public uint ItemId { get; set; }
    [Export] public uint Quantity { get; set; } = 1;
}
```

`NpcItemStack` replaces the shopkeeper's parallel-array hack: an array of typed sub-resources renders as a friendly editable list in the inspector and validates per entry.

### File layout

One `.tres` per NPC, grouped by area, discovered by directory scan (the chunk-grid convention applied to NPCs — no manifest to drift):

```
Resources/Npcs/
  IntroStation/
    intro.rig.tres
    intro.dockmaster_hale.tres
    intro.vex.tres
    intro.chief_marlow.tres
    intro.shopkeeper.tres
```

### Loading and spawning

1. **`NpcDatabase`** (static, mirrors `ItemCatalog`'s role): on first use, walks `res://Resources/Npcs/` recursively with `DirAccess` (handling the exported-build `.remap` suffix exactly like `ChunkManager.DiscoverChunks`), loads every `.tres`, and indexes definitions two ways: by `NpcId` (erroring on duplicates — this is the uniqueness check) and by `(SpawnScenePath, ChunkCoords)`. Definitions come from Godot's resource cache and are shared — **treat them as immutable at runtime**; anything mutable (a `Merchant`, a recruited `CharacterEntity`) is copied out, never written back.
2. **Chunked levels:** `ChunkManager.AddChunk` asks `NpcDatabase` for definitions matching (the level's `SceneFilePath`, chunk coord) and instantiates each as a **child of the chunk node** at `LocalPosition`. That preserves today's lifecycle for free: NPCs stream out with their chunk and re-run their `_Ready` state checks (defeated/recruited) when it streams back in.
3. **Non-chunked scenes** (interiors like `ShopInterior.tscn`): a small `NpcSpawner : Node3D` node added to the scene queries the database by its owner's `SceneFilePath` and spawns all matches on ready.
4. **`Npc` initialization:** the spawner instantiates `def.NpcScene` and calls `Initialize(def)` before adding it to the tree; `Npc` keeps its exports as fallbacks but prefers the definition (`DisplayName`, mesh/`BodyColor`) and exposes `Definition` so role subclasses read their data (`ShopkeeperNpc` builds its `Merchant` from `Credits` + `InitialItems`; `BattleNpc`/quests use `NpcId`).
5. `ShopkeeperNpc`'s `ShopMenu` NodePath export can't survive being spawned from data — resolve the menu at interact time instead (group lookup or via `LevelManager`), which also removes the last editor wiring an NPC needs.

### Character meshes

`CharacterMesh` points at a KayKit character scene (`ThirdParty/KayKit_Adventurers_2.0_FREE/...`). When set, `Npc._Ready` instances it in place of the capsule `MeshInstance3D` and retargets the idle animation; when null, the capsule + `BodyColor` tint stays, so the migration doesn't block on art. `PartyMemberFollower` should reuse the same mesh hookup when a recruit with a mesh joins, but that can trail this plan.

---

## Phase 1 — Data model and authoring

1. `NpcDefinition` + `NpcItemStack` resources; `NpcDatabase` with directory discovery, dual index, and duplicate-id/unknown-item validation (push errors, skip bad files).
2. Author the five existing NPCs as `.tres` files under `Resources/Npcs/IntroStation/`, copying positions from the current chunk/interior scenes (remember chunk-node positions are already chunk-local — they transfer verbatim into `LocalPosition`).
3. Unit tests (the `Tests/` harness) for database indexing, duplicate detection, and item validation.

**Done when:** `NpcDatabase.All` returns five valid definitions and a duplicated `NpcId` fails loudly.

## Phase 2 — Spawning takes over

1. Role scene variants (`Scenes/Npc/RecruitNpc.tscn` etc. — inherited scenes of `Npc.tscn` with the role script), `Npc.Initialize(NpcDefinition)`, and the `ChunkManager.AddChunk` hook + `NpcSpawner` for interiors.
2. `ShopkeeperNpc` builds its `Merchant` from the definition and resolves `ShopMenu` at runtime; delete the parallel-array exports.
3. Remove the hand-placed NPC nodes from `Chunk_0_0.tscn` and `ShopInterior.tscn`.

**Done when:** the Intro station plays identically to today (talk, quest, bounty, recruit, shop, battle all work; defeated/recruited NPCs stay gone across chunk reloads and saves) with zero NPC nodes authored in scene files.

## Phase 3 — Stable ids everywhere

1. Key `GameState.DefeatedNpcs` by `NpcId` (accept legacy display-name entries when reading old saves, write ids; bump `SaveVersion`).
2. Re-key `EnemyCatalog` encounters by `NpcId`, and drive `BountyGiverNpc`'s target from data instead of the `"Vex"` const.
3. Give the recruit flow its id from the definition (add a `PartyCharacterId` export or map `NpcId → CharacterEntity.Id` in one place) — retiring `RecruitNpc.CharacterId`.

**Done when:** renaming an NPC's `DisplayName` in its `.tres` breaks nothing: saves, bounty tracking, and encounters all follow the id.

## Phase 4 — Meshes and polish

1. `CharacterMesh` support in `Npc._Ready` with KayKit characters assigned to the Intro NPCs; capsule fallback stays for meshless definitions.
2. Optional editor affordance: positions are now numbers in an inspector rather than gizmos in a scene. If authoring by coordinates grates, add a `[Tool]` preview or a debug key that prints the player's chunk + chunk-local position to copy into a `.tres`.

## Decisions to settle early

| Decision | Recommendation |
|----------|----------------|
| Id type: `uint` (like items/quests) vs. string slug | **String slug** (`"intro.vex"`). Self-documenting inside `.tres` files and save JSON, no central const list needed to avoid collisions — `NpcDatabase` enforces uniqueness at load. Items/quests keep their `uint`s; nothing requires the schemes to match. |
| Placement in the resource vs. in the scene | **In the resource**, per the goal — one file fully describes an NPC. The cost is losing drag-to-place editing; the Phase 4 debug helper mitigates. NPCs that are *part of a set piece* could still be scene-placed with a definition reference, but don't build that path until something needs it. |
| Registry vs. directory convention | Directory scan of `Resources/Npcs/`, same rationale as chunk discovery: no manifest to drift out of sync. |
| Runtime mutability | Definitions are cached, shared resources — read-only at runtime. Mutable state (merchant stock, recruit entities, defeat flags) is copied into `GameState`/runtime objects. Persisting merchant stock stays future work, unchanged by this plan. |
| Role selection | A `PackedScene` per role on the definition (thin scene variants). Avoids fighting C#'s awkward runtime `SetScript` swap and keeps role scripts free to add scene structure later. **Superseded** by [npc-composition.md](npc-composition.md): the scene variants proved contentless in practice, so `NpcScene` gives way to a single `Npc.tscn` plus an `NpcRole[]` array on the definition. |
