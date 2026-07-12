# Level Chunking — Implementation Plan

Goal: split the game world into 64×64-unit chunks that stream in and out around the player, so areas can grow without loading (or authoring) one giant scene. There is **no procedural generation** — every chunk is a hand-authored scene file, and the directory layout is the world map.

## Where we are

- Levels are single scenes streamed whole through `LoadingScreen` (`LevelManager.StartLevel`).
- `CharacterEntity.ChunkId` already anticipates chunk-scoped world state.
- The `.plan` roadmap lists "Level Chunk Loading" and "World Map".

## Conventions

- **Chunk size:** 64×64 units on X/Z. Height is unconstrained.
- **One scene file per chunk**, organized per area:
  `Scenes/Levels/Chunks/<AreaName>/Chunk_<x>_<z>.tscn`
  (e.g. `Scenes/Levels/Chunks/IntroStation/Chunk_1_0.tscn`; negative coordinates like `Chunk_-1_0.tscn` are valid).
- **Grid ↔ world mapping:** chunk `(x, z)` is centered on world `(x·64, 0, z·64)`; content is authored in chunk-local coordinates within `[-32, 32)` so neighbors tile seamlessly. `ChunkManager.ToChunkCoord(worldPos)` converts back.
- The grid is **discovered from file names** — saving a new `Chunk_<x>_<z>.tscn` into the area directory is all it takes to add terrain; there is no manifest to maintain.
- Level scenes keep the *global* stuff (environment, sun, `Spawn`, and a `ChunkManager` node); chunks hold the *local* stuff (ground, props, pickups, NPCs).

---

## Phase 1 — Streaming core and a 2×2 chunked area *(done — this slice)*

1. `ChunkManager` (`Scripts/World/ChunkManager.cs`): a `Node3D` placed in a level scene with an exported `ChunkDirectory`. Discovers the area's chunks on ready, then each physics frame loads chunks within `LoadRadius` of the player's chunk (threaded, via `ResourceLoader.LoadThreadedRequest`) and frees chunks beyond `UnloadRadius` (the gap prevents border thrashing).
2. Startup ordering: the neighborhood around the player's landing spot (saved position, else `Spawn`) is loaded synchronously in `_Ready` so ground exists before the player's first physics frame.
3. Convert the Intro level: its old content became `IntroStation/Chunk_0_0.tscn`, joined by three new chunks — a cargo yard `(1,0)`, landing pads `(0,1)`, and a hydroponics garden `(1,1)`.

**Done when:** walking around the Intro station streams the four chunks in and out with no hitches, and save/load still restores the player anywhere on the grid.

## Phase 2 — Chunk-scoped world state

1. Persist per-chunk world state in `GameState` (aligns with save plan Phase 5 and NPC plan Phase 4): collected pickups, despawned/defeated NPCs, keyed by area + chunk coordinate.
2. Chunks consult that state when they instance (skip collected pickups, dead NPCs) — fixes today's "pickups respawn on reload".
3. Put `CharacterEntity.ChunkId` to work: derive it from `ToChunkCoord` so saves can record which chunk each party member/NPC occupies.

## Phase 3 — Areas and transitions

1. Multiple area directories (e.g. `DesertWastes/`, `VerdantFields/` to match the battle arena themes), each with its own level scene wrapping a `ChunkManager`.
2. Area transition triggers (a door/gate `Area3D` in a chunk) that call `LevelManager.StartLevel` with the destination level and set `GameState.LocationName`/spawn point.

## Phase 4 — Streaming polish

1. Fade/pop-in mitigation: brief fade or distance fog sized to the load radius so chunk edges never appear raw.
2. Tune radii per area; consider `VisibleOnScreenNotifier3D`/occlusion for dense chunks.
3. Editor affordance: a small tool script or grid gizmo to author chunks in place and verify seams.

## Decisions to settle early

| Decision | Recommendation |
|----------|----------------|
| Chunk origin | Center of the chunk (content in `[-32, 32)`), so a chunk scene previews centered in the editor and mirrors symmetric props cleanly. |
| Registry vs. convention | File-name convention (`Chunk_<x>_<z>.tscn`) — no manifest to drift out of sync; the manager warns on misnamed files. |
| Who owns lights/sky | The level scene. Duplicating a `DirectionalLight3D` or `WorldEnvironment` per chunk would double-light overlapping areas as chunks stream. |
| Player streaming | The player is *not* chunked — `Level` owns it; `ChunkManager` only follows it. |
