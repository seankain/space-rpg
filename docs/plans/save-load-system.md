# Save and Load System — Implementation Plan

Goal: players can create named save slots, see them listed with metadata, load one to resume exactly where they left off, and trust that saves survive crashes and game updates.

## Where we are

The groundwork already committed:

- `SaveData` (`Scripts/Data/SaveData.cs`) — slot metadata (number, timestamps, location name/id), currently created in-memory only by `SaveGameMenu` and never written to disk.
- `GameState` (`Scripts/Data/GameState.cs`) — a party list stub, not yet connected to `SaveData` or any live gameplay data.
- `SaveGameMenu` + `SavedGameMenuItem` — working UI for creating and listing save rows.
- `LoadGameMenu` — an empty panel with no script.

The plan below turns these stubs into a real system in five phases. Each phase is independently shippable and testable.

---

## Phase 1 — Persistence core (SaveManager + disk I/O)

Get a single save slot writing to and reading from disk. No gameplay capture yet — round-tripping metadata is the milestone.

**Work items**

1. Create a `SaveManager` C# autoload (register under `[autoload]` in `project.godot`) that owns all save/load logic. Menus and gameplay code talk to it; nothing else touches the filesystem.
2. Define the on-disk format:
   - One directory per slot under `user://saves/` (e.g. `user://saves/slot_003/`), containing `meta.json` (the `SaveData`) and `state.json` (the `GameState`). Splitting them keeps listing the save menu fast — only `meta.json` is read to populate rows.
   - Serialize with `System.Text.Json`. The data classes are already plain C# POCOs using `System.Numerics.Vector3`, which serializes cleanly — keep Godot types (`Godot.Vector3`, nodes, resources) **out** of the data model and convert at the boundary.
3. Add to `SaveData`: a `SaveVersion` (int) for future migration, and a `SlotDirectory`/`Guid` identifier decoupled from display order.
4. Implement `SaveManager` API:
   - `IReadOnlyList<SaveData> ListSaves()` — enumerate `user://saves/*/meta.json` (use `ProjectSettings.GlobalizePath("user://")` to bridge to `System.IO`).
   - `void Save(SaveData meta, GameState state)` — atomic write: serialize to a temp file, then rename over the old one, so a crash mid-write never corrupts an existing save.
   - `(SaveData, GameState) Load(slotId)` — deserialize and validate.
   - `void Delete(slotId)`.
5. Unit-testable core: keep serialization/IO in a plain class (`SaveRepository`) that the autoload wraps, so it can be tested without booting Godot.

**Done when:** creating a save in the Save menu, quitting, and relaunching shows the save again (even with placeholder game state).

## Phase 2 — Capturing and restoring real game state

Make a save actually represent the running game, and loading recreate it.

**Work items**

1. Extend `GameState` to cover what exists today:
   - `CurrentLevelPath` (scene path or a level id that maps to `LevelManager.LevelScenes`).
   - `PlayerPosition` / `PlayerRotation` (as `System.Numerics` types).
   - `Party` (`List<CharacterEntity>`) — for now a single default player entry created on New Game.
   - `SavePointId` (`Guid`) — matches `SaveData.SaveLocationId` so the friendly location name stops being hardcoded to "Tutorial".
2. Capture: `SaveManager.CaptureState()` walks the live scene — get the `Player` node's `GlobalPosition`/rotation and the current level — and builds a `GameState`. Decide a single owner for "what level is loaded" (recommend `LevelManager`; see the current split between `Level.cs` and `LevelManager` player spawning in [current-progress.md](../current-progress.md)).
3. Restore: `SaveManager.RestoreState(GameState)` loads the level via the existing `LoadingScreen` threaded-load path, then places the player at the saved position **after** the level is instanced (needs a "level ready" signal from `LoadingScreen`/`LevelManager` — also the right moment to fix `LoadingScreen` never clearing `isLoading`).
4. New Game becomes "create fresh `GameState`, then restore it" — one code path for entering a level whether new or loaded.

**Done when:** save mid-level, move the player, load, and the player snaps back to the saved spot; save, quit, relaunch, load, and you're back in the level at the saved spot.

## Phase 3 — Menu integration (Save/Load UI on real data)

Replace the in-memory demo wiring with the SaveManager.

**Work items**

1. `SaveGameMenu`: on open, populate rows from `SaveManager.ListSaves()` instead of a private list; "Create Save" calls `CaptureState()` + `Save()`. Clear and rebuild the `SaveVBox` on each open (rows currently accumulate).
2. Overwrite support: clicking an existing `SavedGameMenuItem` row in the save menu overwrites that slot (update `SaveTime`, keep `SaveCreationTime`), with a confirmation dialog.
3. `LoadGameMenu`: give it a script mirroring `SaveGameMenu` — list saves via the same `SavedGameMenuItem` scene, clicking a row loads it through Phase 2's restore flow and hides the menu. Add a cancel/Escape path like the save menu has.
4. Delete: a delete button per row with confirmation.
5. Menu state rules in `MainMenu`:
   - Save Game disabled until a game is running (partially wired already via `OnNewGameStarted`).
   - Load Game disabled (or showing an empty-state message) when no saves exist.
6. Sort rows by `SaveTime` descending; show relative dates ("2 hours ago") on `SavedGameMenuItem` if cheap to do.

**Done when:** the full loop — New Game → play → Save → quit → relaunch → Load — works entirely through the menus with no debug data.

## Phase 4 — Robustness and polish

Make the system trustworthy and pleasant.

**Work items**

1. **Corruption handling:** a save that fails to deserialize or validate shows as "corrupted" in the list (not a crash), and can be deleted. Log details to Godot's console.
2. **Versioning & migration:** on load, compare `SaveVersion`; write a small migration pipeline (`Func<JsonNode, JsonNode>` per version step) so old saves keep working as `GameState` grows. Refuse (with a clear message) saves from a *newer* version.
3. **Autosave & quicksave:** reserved autosave slot written on level transitions; quicksave/quickload input actions (add to `project.godot` input map). Autosave must reuse the atomic-write path so it can never eat the player's manual save.
4. **Screenshots:** capture a small viewport thumbnail on save, store as `thumb.png` next to `meta.json`, show it in `SavedGameMenuItem`.
5. **Playtime tracking:** accumulate play seconds in `GameState`, display in save rows.
6. **Async save:** if capture+write ever causes a visible hitch, move serialization/IO to a background task with a brief "Saving…" indicator; guard against quitting mid-write.

**Done when:** killing the process mid-save never corrupts existing saves; old-format saves still load after the schema changes; autosave exists.

## Phase 5 — Growing with the game

Not scheduled work — the contract for future systems, so save/load doesn't rot as features land.

- **Rule:** any new system that has runtime state (quests, inventory, NPC/world state, combat-in-progress policy) must (a) store that state in plain serializable classes under `Scripts/Data/`, (b) add it to `GameState`, and (c) bump `SaveVersion` with a migration step.
- Anticipated additions, matching the existing stubs:
  - **Quests:** per-quest `QUESTSUCCESSSTATE` + current `QuestStage` per quest id.
  - **Inventory/equipment:** party inventory as item id + quantity lists; change `CharacterEquipSlots` to reference equipped item ids rather than the `EQUIPSLOT` enum. Keep item *definitions* (names, stats) in game data, saving only ids — saves stay small and item balancing patches apply retroactively.
  - **World state:** opened doors, collected pickups, NPC positions/flags — likely keyed by `CharacterEntity.ChunkId`, which already anticipates this.
  - **Combat:** decide policy early — simplest is "cannot save during combat; loading resumes just before the encounter."

---

## Suggested file layout when complete

```
Scripts/
  Save/
    SaveManager.cs      # autoload; capture/restore orchestration
    SaveRepository.cs   # pure serialization + disk IO (unit-testable)
    SaveMigrations.cs   # version upgrade steps (Phase 4)
  Data/
    SaveData.cs         # slot metadata (+ SaveVersion, slot id)
    GameState.cs        # serializable root of a saved game
    ...
user://saves/           # at runtime, per platform user data dir
  slot_<guid>/
    meta.json
    state.json
    thumb.png           # Phase 4
```

## Risks / decisions to settle early

| Decision | Recommendation |
|----------|----------------|
| JSON vs Godot `ConfigFile`/binary | JSON via `System.Text.Json` — human-readable while debugging, versionable, works with the existing POCO data classes. Revisit binary only if size/perf becomes real. |
| Godot `Resource`-based saves | Avoid: `ResourceLoader` on user-writable files can execute embedded scripts (a known save-file attack vector) and couples the data model to the engine. |
| Who owns level/player spawn | Consolidate into `LevelManager` before Phase 2 — the current dual spawning (`Level.cs` and `LevelManager.AddPlayer`) will fight the restore flow. |
| Save anywhere vs save points | `SaveData.SaveLocationId` hints at save points. Save-anywhere is simpler for Phases 1–3; the schema supports either, so defer the design call. |
