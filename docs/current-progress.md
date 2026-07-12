# Current Progress

_Last updated: 2026-07-12_

space-rpg is a 3rd-person adventure RPG with turn-based combat encounters and quests. Right now the project is in early prototype stage: the main menu flow, character movement in a demo scene, and a set of stubbed data classes exist. Combat, quests, inventory, NPCs, and persistence are not yet implemented.

## Project setup

- **Engine:** Godot 4.6 with the C#/.NET integration (`Godot.NET.Sdk/4.6.0`, .NET 8)
- **Rendering:** GL Compatibility renderer (D3D12 driver on Windows)
- **Physics:** Jolt Physics
- **Main scene:** `Scenes/Menu/Main.tscn`
- **Input actions** (defined in `project.godot`): `Forward`/`Backward`/`Left`/`Right` (WASD), `Jump` (Space), `Interact` (E), `Inventory` (Tab)

## What works today

### Main menu flow (`Scripts/MainMenu.cs`, `Scenes/Menu/MainMenu.tscn`)

The main menu shows New Game / Load Game / Save Game / Options / Quit buttons and swaps between sub-menus by toggling visibility:

- **New Game** (`Scenes/Menu/NewGameMenu.cs`) — shows a start button. Pressing it fires `MainMenu.OnNewGameStarted`, enables the Save Game button, and hides the menu. `LevelManager` listens for this event and kicks off loading of the intro level.
- **Save Game** (`Scripts/SaveGameMenu.cs`) — demonstrates button wiring and scene instancing. "Create Save" builds a `SaveData` record (hardcoded to location "Tutorial"), keeps it in an in-memory list, and instantiates a `SavedGameMenuItem` row (`Scripts/SavedGameMenuItem.cs`, `Scenes/Menu/SavedGameMenuItem.tscn`) showing save number, date, and location name. **Nothing is written to disk** — saves vanish when the game closes.
- **Load Game** (`Scenes/Menu/LoadGameMenu.tscn`) — a plain `Control` with no script; opening it shows an empty panel.
- **Options** — button exists but the handler is not wired and there is no options UI.
- **Quit** — exits the game.

There is also a multiplayer-flavored server browser stub (`Scripts/ActiveGamesList.cs`, `Scripts/ActiveGameRow.cs`) that populates a list with random debug rows (server name, player count, latency). It is not connected to any networking.

### Level loading (`Scripts/LevelManager.cs`, `Scripts/LoadingScreen.cs`)

- `LevelManager` (attached in `Main.tscn`) listens for `OnNewGameStarted` and asks `LoadingScreen` to load `Scenes/Levels/Intro.tscn`.
- `LoadingScreen` uses `ResourceLoader.LoadThreadedRequest` and polls status each frame, driving a progress bar, then instances the loaded scene under `LevelRoot`.
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

These are plain C# classes (not Godot nodes/resources) sketching the future game systems. None of them are used by gameplay code yet, except `SaveData` in the save menu.

| Class | File | Purpose |
|-------|------|---------|
| `SaveData` | `SaveData.cs` | Save-slot metadata: number, creation/save time, location name + id. No reference to actual game state yet. |
| `GameState` | `GameState.cs` | Holds a `List<CharacterEntity>` party. Intended to become the serializable root of a saved game. |
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
- Save/load persistence — see the [save and load system plan](plans/save-load-system.md)
- Options menu
- Multiplayer (the server browser is debug-only UI)

## Known issues / cleanup candidates

- `Spawn.cs` handlers look inverted: `BodyExited` **adds** to the occupier list and `BodyEntered` **removes**, so `IsOccupied` reports the opposite of reality.
- `CameraController` clamps tilt with `TiltMax = 75` against `Rotation.X`, which is in **radians**, so the clamp never engages; the clamp also reads `this.Rotation.X` instead of the just-updated local `rot.X`.
- `LoadingScreen` never sets `isLoading = false` or hides itself after the scene finishes loading, so `LoadThreadedGetStatus`/instancing can re-run every frame.
- `LoadingScreen.NextScenePath` defaults to `res://Scenes/Intro.tscn`, but the scene actually lives at `res://Scenes/Levels/Intro.tscn`.
- `MainMenu.cs` imports `Microsoft.VisualBasic`, which is unused and non-portable.
- Both `Level.cs` and `LevelManager.AddPlayer` instantiate players; the long-term owner of spawning should be decided (relevant to load-game flow).
- `SaveGameMenu.AddSaveToDisplay` re-loads the `PackedScene` by path even though the export already provides it (`GD.Load` result is discarded in `ActiveGamesList` similarly).
