# World Map — Implementation Plan

Goal: fill the empty **Map** tab in the in-game menu with a top-down map of the current area, assembled from one baked overhead image per level chunk. The images are rendered in the Godot editor by a bake tool and committed to the repo, so the map can be regenerated any time chunk scenes change. Landmarks — portals and stores — appear on the map as icons.

## Where we are

- `InGameMenu.tscn` already has a `Map` tab (tab index 3), but it is an empty `Control` with no script; `InGameMenu.cs` is a stub.
- The world is chunked (see `level-chunking.md`): each area is a directory of hand-authored scenes `Scenes/Levels/Chunks/<AreaName>/Chunk_<x>_<z>.tscn`, chunk `(x, z)` centered on world `(x·64, 0, z·64)`, grid discovered from file names by `ChunkManager`. Two areas exist today: `IntroStation` (2×2) and `World1` (1 chunk).
- Chunks stream in and out around the player, so at any moment most of an area is **not** in the scene tree — the map cannot be drawn from live nodes alone. This is the core reason to bake.
- Portals (`Scripts/World/Portal.cs`) are nodes authored inside chunk scenes, with a `TargetDisplayName` suitable for a map label.
- Stores are NPCs with a `ShopkeeperRole` in `NpcDefinition.Roles`; each definition already records `ChunkCoords` and chunk-local `LocalPosition`, queryable through `NpcDatabase` without loading any chunk scene.
- The `.plan` roadmap lists "World Map".

## Conventions

- **One baked image per chunk:** `Resources/Maps/<AreaName>/Chunk_<x>_<z>.png`, mirroring the chunk scene naming so the map grid is discovered from file names exactly like `ChunkManager.DiscoverChunks` — no manifest for imagery.
- **Resolution:** 256×256 px per chunk (4 px per world unit). Sharp enough to read structures, small enough to commit; an area of a dozen chunks stays under a few MB.
- **Coordinate mapping:** world `(x, z)` → map pixel `((x − minChunkX·64 + 32) · 4, (z − minChunkZ·64 + 32) · 4)` relative to the stitched image's top-left. All mapping math lives in one small static helper (`MapProjection`) shared by the bake tool and the map UI so the two can never disagree.
- **Landmarks manifest:** `Resources/Maps/<AreaName>/landmarks.json` — baked alongside the images, listing scene-authored landmarks (portals, doors) with type, display name, and world position. Store landmarks are *not* baked; they come from `NpcDatabase` at runtime, because NPC data is the source of truth for where shopkeepers are and already loads without chunk scenes.
- **Map layer:** 3D nodes that should never appear on the map (interaction prompts, VFX, NPC bodies) are excluded by camera cull mask, not by node deletion, so the bake never mutates chunk scenes.

---

## Phase 1 — Editor bake tool: top-down chunk capture *(done — this slice)*

1. `Scripts/Editor/MapBaker.cs`: an `EditorScript` (run via File → Run, promoted to an `EditorPlugin` menu item in Phase 4) that iterates every area directory under `Scenes/Levels/Chunks/`, walking the grid through a now-static `ChunkManager.DiscoverChunks` so baker and streaming share one file-name convention.
2. For each `Chunk_<x>_<z>.tscn`: instantiate it into an offscreen own-world `SubViewport` (256×256, dark "space" background color via the camera environment), with a fixed `DirectionalLight3D` plus ambient fill for consistent shading, and an orthographic `Camera3D` looking straight down (`size = 64`, image top = world −Z, cull mask excluding the reserved hidden-from-map layer 20).
3. Force the renderer to draw (twice — once to upload freshly instanced meshes, once for the settled frame), grab the viewport image, and save the PNG to `Resources/Maps/<AreaName>/`. All camera/light settings are constants so re-bakes are deterministic and diff cleanly.
4. While each chunk is instantiated, walk its nodes and collect landmark entries (Phase 3 consumes these): every `Portal` (type `portal`, name from `TargetDisplayName`) and entrance `Door` (type `door`), with positions converted to world space via the chunk's grid coordinate. Write `landmarks.json` per area (`MapLandmarksFile`, engine-free and unit-tested alongside `MapProjection`).

**Done when:** running the bake produces a PNG per chunk for `IntroStation` and `World1` plus a `landmarks.json` per area, and re-running after editing a chunk scene visibly updates that chunk's image and nothing else.

## Phase 2 — Map tab renders the stitched area map

1. `Scripts/MapMenu.cs` on the `Map` tab control, following the pattern of the sibling tab scripts (`QuestLogMenu`, `InventoryMenu`).
2. On tab open, resolve the current area: find the running level's `ChunkManager` and take the basename of its `ChunkDirectory` (falls back to a "no map available" label in interiors and unchunked scenes).
3. Discover `Resources/Maps/<AreaName>/Chunk_*.png` by file name, and place one `TextureRect` per chunk at its grid position inside a container that supports pan (drag) and zoom (wheel / +/− buttons). Chunks with no baked image render as an empty grid cell, not an error.
4. Player marker: an arrow icon positioned via `MapProjection` from the player's `GlobalPosition` and rotated by the player's Y heading, refreshed each frame while the tab is visible. Add an area-name header (`GameState.LocationName`).

**Done when:** opening the Map tab in the intro station shows the four chunk images seamlessly tiled, with the player arrow at the right spot and heading, live-updating; pan and zoom work; opening it inside an interior shows the fallback message instead of erroring.

## Phase 3 — Landmark icons

1. Icon assets: two new small PNGs in `Icons/` (portal swirl, store tag), plus the arrow from Phase 2. Simple flat glyphs at ~32 px, drawn to read at map scale.
2. `MapMenu` merges two landmark sources on open:
   - **Baked:** parse the area's `landmarks.json` for portals and doors.
   - **Live data:** query `NpcDatabase` for the current level's definitions whose `Roles` contain a `ShopkeeperRole`; world position is `ChunkCoords·64 + LocalPosition`.
3. Each landmark becomes an icon `TextureRect` overlaid at its `MapProjection` position, scaling its *position* with zoom but keeping constant on-screen icon size. Hovering (or focusing, for controller) shows the display name — "Travel to World 1", "Shopkeeper".

**Done when:** the intro station map shows a store icon at the shopkeeper and a portal icon at the portal, each in the correct map position at any zoom, with a readable name on hover.

## Phase 4 — One-click regeneration in the editor

1. Promote `MapBaker` to a proper `EditorPlugin` (`addons/map_baker/`): a **Project → Tools → Bake World Maps** menu item that re-bakes everything, and a per-area option when that's ever slow.
2. Staleness detection instead of silent drift: the bake writes each source scene's file hash into a small `bake_manifest.json` per area. A unit test in `Tests/` walks every `Chunk_*.tscn`, and fails with "map image missing or stale — run Bake World Maps" when a chunk has no image or its recorded hash doesn't match. This makes CI the reminder, without needing the editor in CI.
3. Optional convenience: hook the editor's `resource_saved` signal in the plugin to auto-rebake just the saved chunk scene, so the common author-loop (edit chunk → save → check map) needs zero clicks.

**Done when:** after editing a chunk, either saving auto-refreshes its map image or the test suite fails until the menu item is run; a fresh clone with stale bakes cannot pass CI unnoticed.

## Phase 5 — Polish and stretch

1. **Fog of war:** record visited chunk coordinates per area in `GameState` (aligns with the save plan's world-state section); unvisited chunks render darkened or hidden. Landmarks appear once their chunk has been visited.
2. **Minimap HUD:** a small always-on corner map reusing the same baked textures and `MapProjection` — the bake investment pays twice.
3. **Quest markers:** when the quest system gains target locations, draw them through the same landmark-icon path.

## Decisions to settle early

| Decision | Recommendation |
|----------|----------------|
| Bake in editor vs. capture at runtime | Bake and commit PNGs. Streaming means unloaded chunks have no live nodes to capture, runtime capture would cost frame time, and baked images are deterministic and reviewable in PRs. |
| Landmark discovery | Split by source of truth: portals/doors are scene nodes → scan at bake time into `landmarks.json`; stores are NPC data → read `NpcDatabase` live at runtime, so moving a shopkeeper's `.tres` never leaves a stale manifest. |
| Image layout | One PNG per chunk, stitched by the UI — matches the "directory is the world map" convention, keeps re-bakes incremental, and avoids re-writing a giant atlas when one chunk changes. |
| Hiding non-map nodes | Camera cull mask against a dedicated visual layer for prompts/VFX. The bake must never edit or filter chunk scenes destructively. |
| Manifest format | JSON, matching the save system's serialization habits; parsed with the same tooling, testable without the engine. |
| Where mapping math lives | A single `MapProjection` helper used by both baker and UI, so pixel↔world conversion can't drift between them. |
