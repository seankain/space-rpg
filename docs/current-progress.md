# Current Progress

_Last updated: 2026-07-12_

space-rpg is a 3rd-person adventure RPG with turn-based combat encounters and quests. Right now the project is in early prototype stage: the main menu flow, character movement in a demo scene, a working save/load system, and a set of stubbed data classes exist. Combat, quests, inventory, and NPCs are not yet implemented.

## Project setup

- **Engine:** Godot 4.6 with the C#/.NET integration (`Godot.NET.Sdk/4.6.0`, .NET 8)
- **Rendering:** GL Compatibility renderer (D3D12 driver on Windows)
- **Physics:** Jolt Physics
- **Main scene:** `Scenes/Menu/Main.tscn`
- **Input actions** (defined in `project.godot`): `Forward`/`Backward`/`Left`/`Right` (WASD), `Jump` (Space), `Interact` (E), `Inventory` (Tab)

## What works today

### Main menu flow (`Scripts/MainMenu.cs`, `Scenes/Menu/MainMenu.tscn`)

The main menu shows New Game / Load Game / Save Game / Options / Quit buttons and swaps between sub-menus by toggling visibility:

- **New Game** (`Scenes/Menu/NewGameMenu.cs`) — shows a start button. Pressing it fires `MainMenu.OnNewGameStarted`; `LevelManager` creates a fresh `GameState` via `SaveManager.StartNewGame()` and loads the intro level.
- **Save Game** (`Scripts/SaveGameMenu.cs`) — lists real saves from disk as clickable `SavedGameMenuItem` rows. "New Save" captures the running game and writes a slot; clicking an existing row overwrites it (with confirmation); each row has a delete button (with confirmation).
- **Load Game** (`Scripts/LoadGameMenu.cs`) — lists the same rows; clicking one restores its `GameState` and loads the saved level, placing the player at the saved position. Corrupt saves show an error dialog instead of crashing.
- **Options** — button exists but the handler is not wired and there is no options UI.
- **Quit** — exits the game.

### Save/load system (`Scripts/Save/`)

Implemented per phases 1–3 of the [save/load plan](plans/save-load-system.md):

- `SaveManager` — autoload (registered in `project.godot`) owning the running `GameState` and all save/load orchestration: new game, capture (player transform via the `"Player"` node group), create/overwrite/load/delete.
- `SaveRepository` — engine-free serialization and disk IO: one directory per slot under `user://saves/slot_<guid>/` with `meta.json` (`SaveData`) + `state.json` (`GameState`), written atomically (temp file + rename), JSON via `System.Text.Json`. Corrupt or newer-versioned saves are skipped in listings with a warning.
- Restore flow: `LevelManager.StartLevel` is the single entry point for both new and loaded games — it clears `LevelRoot` and streams the level through `LoadingScreen`; `Level.AddPlayer` places the player at the saved position when one exists, otherwise at the level's `Spawn` marker.

There is also a multiplayer-flavored server browser stub (`Scripts/ActiveGamesList.cs`, `Scripts/ActiveGameRow.cs`) that populates a list with random debug rows (server name, player count, latency). It is not connected to any networking.

### Level loading (`Scripts/LevelManager.cs`, `Scripts/LoadingScreen.cs`)

- `LevelManager` (attached in `Main.tscn`) reacts to new-game and load-game events by calling `StartLevel`, which clears `LevelRoot` and streams the level in via `LoadingScreen`; the mouse is captured when loading completes.
- `LoadingScreen` uses `ResourceLoader.LoadThreadedRequest` and polls status each frame, driving a progress bar, then instances the loaded scene under `LevelRoot`, hides itself, and raises `LoadCompleted`. Load failures log an error instead of spinning forever.
- `LevelManager` also handles global input: **Escape** toggles the main menu (and mouse capture), **Tab** toggles the in-game menu.
- `ChangeLevel(int)` exists for swapping levels from an exported `LevelScenes` array but nothing calls it yet.

### Demo level and player (`Scenes/Levels/Intro.tscn`, `Scripts/Player.cs`)

- `Level.cs` instances the player scene on ready and moves it to a `Spawn` node's position.
- `Player.cs` is a `CharacterBody3D` with WASD movement, gravity, and jumping, plus a simple two-state animation switch (Idle/Running) that is noted in-code as a placeholder for a proper `AnimationTree`.
- `CameraController.cs` provides mouse freelook (tilt clamped). State machine scaffolding exists for snap-to-rear and idle-spin camera behaviors but the `_Process` body driving it is commented out.
- `Spawn.cs` tracks whether bodies occupy a spawn point via an `Area3D` trigger.
- An NPC scene (`Scenes/Npc.tscn`) exists but has no behavior script.
- `PlayerHud.cs` and `InGameMenu.cs` are empty shells.

### Data model stubs (`Scripts/Data/`)

These are plain C# classes (not Godot nodes/resources) sketching the future game systems. `SaveData` and `GameState` are live (used by the save/load system); the rest are not used by gameplay code yet.

| Class | File | Purpose |
|-------|------|---------|
| `SaveData` | `SaveData.cs` | Save-slot metadata: version, slot id, number, creation/save time, location name + id. |
| `GameState` | `GameState.cs` | Serializable root of a saved game: current level path, location, player transform, and the `List<CharacterEntity>` party. |
| `CharacterEntity` | `CharacterEntity.cs` | Character record: id, name, chunk id, position (`System.Numerics.Vector3`), level, XP, HP/MP, stats, equip slots, active status effects. |
| `CharacterStats` | `CharacterStats.cs` | Classic six-stat block (STR/INT/CON/DEX/WIS/CHA). |
| `Item`, `EquippableItem`, `Weapon`, `Armor` | `Item.cs`, `EquippableItem.cs` | Item hierarchy with equip-slot validity, physical damage/defense. |
| `CharacterEquipSlots`, `EQUIPSLOT` | `EquipSlots.cs` | Equipment slots (head, eyes, hands, chest, legs). Note: slots are currently typed as the `EQUIPSLOT` enum rather than referencing an equipped `Item`. |
| `Quest`, `QuestStage`, `QuestPrereqFlag`, `QUESTSUCCESSSTATE` | `Quest.cs` | Quest definitions with prerequisite flags and success states. |
| `ActiveStatusEffect`, `STATUSEFFECT` | `StatusEffect.cs` | Timed status effects (poison, sleep, confusion). |

## Not yet implemented

- Turn-based combat encounters
- Quest tracking / journal
- Inventory and equipment UI (Tab opens an empty `InGameMenu`)
- NPC behavior and dialogue
- Save/load extras — autosave/quicksave, migrations, thumbnails, playtime; see the [save and load system plan](plans/save-load-system.md)
- Options menu
- Multiplayer (the server browser is debug-only UI)

## Known issues / cleanup candidates

- `Spawn.cs` handlers look inverted: `BodyExited` **adds** to the occupier list and `BodyEntered` **removes**, so `IsOccupied` reports the opposite of reality.
- `CameraController` clamps tilt with `TiltMax = 75` against `Rotation.X`, which is in **radians**, so the clamp never engages; the clamp also reads `this.Rotation.X` instead of the just-updated local `rot.X`.
- `ActiveGamesList.AddActiveGameRow` calls `GD.Load` and discards the result.
- Pressing Escape on the main menu before any game has started hides the menu over an empty scene.
