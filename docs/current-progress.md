# Current Progress

_Last updated: 2026-07-19_

space-rpg is a 3rd-person adventure RPG with turn-based combat encounters and quests. Right now the project is in early prototype stage: the main menu flow, character movement in a demo scene, a working save/load system, interactable NPCs with stub dialogue (recruitment, a fetch quest, a battle challenge), a playable turn-based battle system, chunk-streamed levels (64×64-unit hand-authored chunks), a party system (roster rules, followers walking behind the leader, a management tab), an in-game menu with a quest log and party inventory management (use/equip/drop), an enterable shop interior with a buy/sell shopkeeper (party credits), and a set of stubbed data classes exist.

## Project setup

- **Engine:** Godot 4.6 with the C#/.NET integration (`Godot.NET.Sdk/4.6.0`, .NET 8)
- **Rendering:** GL Compatibility renderer (D3D12 driver on Windows)
- **Physics:** Jolt Physics
- **Main scene:** `Scenes/Menu/Main.tscn`
- **Input actions** (defined in `project.godot`): `Forward`/`Backward`/`Left`/`Right` (WASD), `Jump` (Space), `Interact` (E), `Inventory` (Tab)
- **Tests:** `Tests/SpaceRpg.Tests.csproj` — xunit over the engine-free sources (`Scripts/Data/`, `SaveRepository`), excluded from the Godot build; run with `dotnet test Tests`

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

### Level chunking (`Scripts/World/ChunkManager.cs`, `Scenes/Levels/Chunks/`)

Phase 1 of the [level chunking plan](plans/level-chunking.md) — the world streams in 64×64-unit hand-authored chunks (no procedural generation):

- Chunks are scene files organized per area as `Scenes/Levels/Chunks/<AreaName>/Chunk_<x>_<z>.tscn`; chunk `(x, z)` is centered on world `(x·64, 0, z·64)` and its content is authored in local coordinates within `[-32, 32)`. The grid is discovered from file names — no manifest.
- `ChunkManager` (a node in the level scene, with the area directory exported) loads chunks within `LoadRadius` of the player's chunk each physics frame (threaded) and frees chunks beyond `UnloadRadius`; the gap prevents border thrashing. The starting neighborhood (saved position, else `Spawn`) loads synchronously on ready so ground exists before the player's first physics frame.
- The level scene keeps global content (sun, sky, `Spawn`, `ChunkManager`); chunks hold local content (ground, props, pickups, NPCs).
- The Intro station is a 2×2 chunked area: the old Intro content is `IntroStation/Chunk_0_0.tscn` (plaza with the NPCs and Maguffin Cube), plus a cargo yard `(1,0)`, landing pads `(0,1)`, and a hydroponics garden `(1,1)`.

### Demo level and player (`Scenes/Levels/Intro.tscn`, `Scripts/Player.cs`)

- `Intro.tscn` is now a thin level shell (light, sky, `Spawn`, `ChunkManager`) whose ground and props stream in from the `IntroStation` chunk directory (see Level chunking above).
- `Level.cs` instances the player scene on ready and moves it to a `Spawn` node's position.
- `Player.cs` is a `CharacterBody3D` with WASD movement, gravity, and jumping, plus a simple two-state animation switch (Idle/Running) that is noted in-code as a placeholder for a proper `AnimationTree`.
- `CameraController.cs` provides mouse freelook (tilt clamped). State machine scaffolding exists for snap-to-rear and idle-spin camera behaviors but the `_Process` body driving it is commented out.
- `Spawn.cs` tracks whether bodies occupy a spawn point via an `Area3D` trigger.
- `PlayerHud.cs` and `InGameMenu.cs` are empty shells (the in-game menu's Quests, Party, and Inventory tabs are driven by their own `QuestLogMenu`/`PartyMenu`/`InventoryMenu` scripts; the Map tab is still empty).

### NPCs and stub dialogue (`Scripts/Npc/`, `Scripts/Dialogue/`)

First slice of the [NPC plan](plans/npc-system.md) (Phase 1 plus a taste of Phase 2's placement), with a homegrown dialogue box standing in until the [Yarn Spinner integration](plans/npc-dialogue-yarn.md):

- `Npc` (`Scripts/Npc/Npc.cs`) — the one NPC script, on the one `Scenes/Npc.tscn` ([composition plan](plans/npc-composition.md) Phase 2): a code-built `Area3D` interaction zone, the shared `InteractionPrompt` ("[E] Talk to ..."), face-the-player on interact, and a conversation composed from the definition's `NpcRole` resources — no available roles waves the player off, one plays its dialogue directly, several offer a choice menu. Roles (`Scripts/Npc/Roles/`: `QuestGiverRole`, `BountyGiverRole`, `ShopkeeperRole`, `RecruitRole`, `ChallengerRole`) are stateless shared templates; per-NPC mutable state (a shop's `Merchant`) is created via `CreateRuntimeState` and owned by the node, and roles can veto spawning entirely (`ShouldSpawn`: recruited members, despawned challengers) or sit gated behind a quest state (`RequiredQuestId`).
- `DialogueManager` — autoload (registered in `project.godot`) owning the "in dialogue" mode: shows one `DialogueLine` at a time in a bottom-screen box (speaker, text, choice buttons or an "[E] Continue" hint), releases the mouse while talking, and restores capture on end. `DialogueLine`/`DialogueChoice` (`Scripts/Dialogue/Dialogue.cs`) are a minimal hand-authored tree — NPC scripts build them in code. Player movement/jump, pickups, NPC re-interaction, and camera look are all suppressed while a dialogue is active.
- Four demo NPCs live in the Intro level:
  - **Rig** (`RecruitRole`) — asks "Can I join up with you?"; Yes adds him to `GameState.Party` through `PartyManager` as a `CharacterEntity` (id 2), despawns the world body when the conversation closes, and spawns a `PartyMemberFollower` in his place. Already-recruited saves skip spawning him on reload, and a full party gets a "full crew" line instead of the offer.
  - **Dockmaster Hale** (`QuestGiverRole`) — offers the "Return the Maguffin" fetch quest; branches for offer/decline, in-progress reminder, turn-in (removes the Maguffin Cube from inventory, marks the quest `Success`), and post-completion thanks. Handing it in works even if the cube was picked up before taking the quest — and the turn-in demand can be *refused at swordpoint*: refusing twice starts a battle through the shared `DialogueActions.StartBattle` (no `ChallengerRole` involved). Beating Hale sticks in `GameState.DefeatedNpcs` — he stays in the world, stops pressing the point, and the quest line keeps working.
  - **Vex** (`ChallengerRole`) — challenge dialogue whose "Settle it" choice starts a real turn-based battle via `BattleManager.StartBattle`; winning records him in `GameState.DefeatedNpcs` (save version 6) and despawns him (`DespawnOnDefeat`), and defeated challengers skip spawning on reload.
  - **Chief Marlow** (`BountyGiverRole` + `RecruitRole`) — offers the "Clear the Deck" side quest: defeat Vex, then report back to be paid a Maintenance Keycard (quest item). The turn-in checks `GameState.DefeatedNpcs`, so it works even if Vex was beaten before taking the bounty. His second role is quest-gated (`RequiredQuestId`): once the bounty hits `Success`, talking to him opens the multi-role choice menu and he can join the party (member id 3) — the composition plan's quest-giver-turned-recruit proof.
- Quest progress lives in `GameState.Quests` (`QuestProgress` records against `QuestCatalog` definitions) and round-trips through saves (save version 3; older saves load with an empty quest log). Quest/party/join feedback is `GD.Print`-only until the HUD grows toasts.

### Party system (`Scripts/Data/PartyManager.cs`, `Scripts/PartyMemberFollower.cs`, `Scripts/PartyMenu.cs`)

Phases 1–3 of the [party plan](plans/party-system.md):

- `PartyManager` — engine-free roster rules wrapped around the live `GameState.Party` list: index 0 is the leader (the controlled character), max active size 4, `TryAddMember` (no duplicates, no overflow), `RemoveMember` (the last member can't leave; removing the leader promotes the next), `SetLeader`, and `Move` for one-step reordering. Covered by xunit tests (`Tests/PartyManagerTests.cs`), plus a multi-member save round-trip test (`Tests/PartySaveRoundTripTests.cs`).
- `PartyMemberFollower` (`Scenes/PartyMemberFollower.tscn`) — every member beyond the leader walks the level as a follower: AI-driven, wearing the member's NPC rig wrapper (`Scenes/Characters/Rigs/`, resolved by display name; the player's Knight rig is the fallback), seeking the player with per-member spacing (direct pursuit; no navmesh yet) and a floating name label. Followers are physical but non-blocking (collision layer 0), and a catch-up teleport self-heals stale positions, snags, and falls. `Level.AddPlayer` spawns them on level load/restore; `SaveManager.CaptureState` persists each follower's position through the member's existing `CharacterEntity.Position` (no save-format change), and positions are only trusted on restore when they plausibly belong to the loaded level — otherwise followers fall in line behind the leader.
- `PartyMenu` — drives the Party tab of the in-game menu (Tab): the roster list (leader starred, level/HP/PP inline) and a member sheet (level/XP, vitals, six stats, equipped gear) with the management controls: **Set Leader**, **Move Up**/**Move Down** (order feeds the future combat turn layout; moving into first place changes the leader), and **Dismiss** behind a confirmation dialog. Dismissing strips the member's equipped gear back into the shared inventory (a re-recruit builds a fresh `CharacterEntity`), and any roster change rebuilds the follower line behind the player in place. A dismissed Rig stands at his dock spot again next time the area loads.

### Turn-based battles (`Scripts/Battle/`)

Phase 1 of the [battle plan](plans/battle-system.md) — a playable JRPG-style combat loop:

- `BattleManager` — autoload owning the field ↔ battle mode switch: `StartBattle(opponentName, onVictory)` pauses the scene tree, hides the running level (its state survives untouched), builds a `BattleScene` above the field, and swaps the camera. Victory restores the field and fires the callback (the dialogue that started the fight decides the aftermath — Vex despawns, Hale stays standing); defeat is a game over — the level is torn down and `LevelManager.ShowGameOverLoadMenu()` puts the player in front of the Load Game menu to restore a previous save.
- `BattleScene` — code-built generic arena (flat ground plane, directional light, procedural-sky environment override on the battle camera) with billboarded name/HP labels for combatants. Fighters wear their NPC definition's `Rig` wrapper (the player uses the Knight rig; recruits resolve by display name, enemies by `EnemyDefinition.NpcId`) with looping idle and hold-the-pose death animations; fighters without a resolvable rig keep the tinted placeholder capsule, which keels over when downed. Runs the turn loop: rounds repeat until a side is wiped, all living combatants acting once per round in Dexterity order (party wins ties). Enemy AI favors a damage power when affordable, otherwise attacks a random living party member.
- `BattleHud` — code-built UI (same stub style as the dialogue box): top message bar, party HP/PP readout, and an action menu that walks **Attack / Power / Item** down to a target pick, awaited by the turn loop as a `Task`. Also owns the Game Over panel.
- `BattleArenaTheme` — battle areas are themed by the current world area: `GameState.LocationName` maps to ground/sky/sun colors (Station Deck, Desert Wastes, Verdant Fields; unknown areas fall back to Station Deck).
- `Power`/`PowerCatalog` — powers cost power points (the `MagicPoints` field on `CharacterEntity`, shown as PP): Plasma Surge (damage) and Nano Mend (heal), known by every party member for now.
- `EnemyCatalog` — encounters keyed by the challenging NPC's stable id (`"intro.vex"` → Vex + a Dock Drone; `"intro.dockmaster_hale"` → a sturdy one-on-one for the Maguffin refusal branch); unknown ids get a generic one-enemy fight wearing the challenger's name and rig. Duplicate enemy names are disambiguated (A/B/...).
- `BattleCombatant`/`BattleAction` — runtime battle state; party combatants wrap their `CharacterEntity` and write HP/PP/XP back on victory (downed members revive at 1 HP). Items used in battle are consumables from the shared party inventory and are consumed on use.
- Action VFX (`Scripts/Vfx/`) — every battle action plays a one-shot particle effect on its target: melee spark burst, Plasma Surge's plasma detonation, Nano Mend's rising repair motes, and an item-use shimmer. `VfxLibrary` is scene-agnostic (`Spawn(VfxId, parent, position)` attaches a self-freeing `OneShotVfx` to any `Node3D`, battle or field) with effects built in code; powers pick their effect via `Power.Vfx`, and the engine-free `VfxId` enum keeps the data layer testable. Packaged effect scenes can later replace the code-built ones per id.

### Inventory basis (`Scripts/Pickup.cs`, `Scripts/InventoryMenu.cs`, `Scenes/Items/MaguffinCube.tscn`)

First slice of the [inventory plan](plans/inventory-system.md):

- `Pickup` — an `Area3D` carrying an item id + quantity; pressing **Interact** (E) while in range adds the item to the party inventory and frees the node. Pickups slowly spin for visibility. While the player is in range, a world-space prompt (e.g. "[E] Pick up Maguffin Cube") floats above the item. Collected pickups are *not* yet persisted in world state, so reloading a save respawns them.
- `InteractionPrompt` (`Scripts/InteractionPrompt.cs`) — reusable billboarded `Label3D` hint for interactable objects. Resolves the key glyph from the `InputMap` at runtime (so rebinding Interact updates the hint) and renders fixed-size with no depth test so it stays readable at any distance. Shared with the NPC "[E] Talk" prompt, and its key-glyph resolver also feeds the dialogue box's continue hint.
- `Scenes/Items/MaguffinCube.tscn` — a glowing purple cube pickup for the Maguffin Cube quest item; one is placed in the Intro level near spawn.
- `InventoryMenu` — drives the Inventory tab of the in-game menu (Tab): a `TabBar` filters stacks by category (All / Weapons / Armor / Consumables / Quest Items), an `ItemList` shows stacks with quantities, and a details panel shows the selected item's name, description, and stats (damage/defense/heal). The details panel also manages the party: a dropdown picks a party member (with live HP readout) and **Use** (consumables heal the member, consuming one), **Equip** (weapons/armor go into the member's matching slot; anything displaced returns to the inventory), and **Drop** (discards one; quest items can't be dropped) act on the selected stack, with the member's current equipment listed alongside. Refreshes whenever the tab becomes visible.

### Enterable interiors and shops (`Scripts/World/Door.cs`, `Scenes/Levels/ShopInterior.tscn`, `Scripts/ShopMenu.cs`)

Prototype of interior dwellings the player can walk into, plus the first merchant:

- `Door` (`Scripts/World/Door.cs`) — a doorway the player activates with **Interact** (E), built on the same code-built zone + `InteractionPrompt` plumbing as NPCs/pickups. An entrance door records the player's position and current level into `GameState`'s return-point fields, then swaps levels through `LevelManager.StartLevel`, so the loading screen shows; an exit door (`ReturnsToPrevious`) consumes the return point to put the player back outside. Because the return point lives in `GameState`, saving inside an interior and loading later still walks back out correctly (save version 5).
- `Scenes/World/ShopBuilding.tscn` — placeholder exterior: a large cube with a flat door plane, placed in the Intro plaza chunk; its door loads the shop interior.
- `Scenes/Levels/ShopInterior.tscn` — a small hollow-box interior level (CSG room, counter, spawn marker, exit door) with a shopkeeper NPC and the shop UI declared in-scene.
- `ShopkeeperRole` (`Scripts/Npc/Roles/ShopkeeperRole.cs`) — the merchant role on Trader Moss's definition: builds a `Merchant` from the definition's `Credits`/`InitialItems` as per-NPC runtime state, and talking offers to open the trading screen. Merchant stock/credits reset when the interior reloads (not yet persisted).
- `ShopMenu` (`Scripts/ShopMenu.cs`, `Scenes/Menu/ShopMenu.tscn`) — scene-declared trading screen: Buy tab lists the merchant's stock, Sell tab the party inventory, with a details/price panel and result messages. Pricing rules live in the engine-free `Trade` class (buy at catalog `Item.Value`, sell back at half; quest items and zero-value items can't be sold), executing against `GameState.Credits` (the party's shared credits, new games start with 250) and the `Merchant`'s credits/stock. While the shop is open, movement and other interactions are locked the same way as during dialogue, and Escape closes the menu.

### Quest log (`Scripts/QuestLogMenu.cs`)

First slice of the [quest plan](plans/quest-system.md)'s Phase 3 journal:

- `QuestLogMenu` — drives the Quests tab of the in-game menu (Tab): every quest the player has picked up, grouped into Main Quests / Side Quests (in progress) and Completed / Failed sections in one list; selecting a quest shows its title, main/side + status line, and description. Shows a "no quests yet" hint until the first quest is taken. Stage subtitles/objectives wait on the quest plan's stage work.

### Data model stubs (`Scripts/Data/`)

These are plain C# classes (not Godot nodes/resources) sketching the future game systems. `SaveData` and `GameState` are live (used by the save/load system); the rest are not used by gameplay code yet.

| Class | File | Purpose |
|-------|------|---------|
| `SaveData` | `SaveData.cs` | Save-slot metadata: version, slot id, number, creation/save time, location name + id. |
| `GameState` | `GameState.cs` | Serializable root of a saved game: current level path, location, player transform, interior return point, party `Credits`, the `List<CharacterEntity>` party, the shared `Inventory`, and `Quests` progress (with get/set quest-state helpers). |
| `CharacterEntity` | `CharacterEntity.cs` | Character record: id, name, chunk id, position (`System.Numerics.Vector3`), level, XP, HP/MP, stats, equip slots, active status effects. |
| `PartyManager` | `PartyManager.cs` | Roster rules over `GameState.Party`: leader at index 0, max size 4, add/remove/reorder. Live — used by recruiting and the Party tab. |
| `CharacterStats` | `CharacterStats.cs` | Classic six-stat block (STR/INT/CON/DEX/WIS/CHA). |
| `Item`, `ConsumableItem`, `QuestItem`, `ItemCategory` | `Item.cs` | Abstract item base (id, name, description, stack cap, credit `Value`) plus consumable/quest subtypes; every item maps to an `ItemCategory` (Weapon / Armor / Consumable / QuestItem) used by the inventory UI. |
| `EquippableItem`, `Weapon`, `Armor` | `EquippableItem.cs` | Equippable subtypes with equip-slot validity, physical damage/defense. |
| `Inventory`, `ItemStack` | `Inventory.cs` | Party-shared inventory on `GameState`: list of id+quantity stacks with add/remove/count honoring per-item stack caps. Live — serialized in saves (save version 2; v1 saves load with an empty inventory). |
| `ItemCatalog` | `ItemCatalog.cs` | Static registry of item definitions keyed by id (saves reference ids only). Ships the Maguffin Cube and Maintenance Keycard quest items plus one sample item per category. |
| `CharacterEquipSlots`, `EQUIPSLOT` | `EquipSlots.cs` | Per-character equipment: one nullable equipped-item id per slot (head, eyes, hands, chest, legs) with get/set/equip-swap helpers. Live — serialized in saves (save version 4; older saves load with empty slots). |
| `Quest`, `QuestStage`, `QuestPrereqFlag`, `QUESTSUCCESSSTATE` | `Quest.cs` | Quest definitions with prerequisite flags and success states. `QuestProgress` (quest id + state + stage) is live — stored in `GameState.Quests` (save version 3). |
| `QuestCatalog` | `QuestCatalog.cs` | Static registry of quest definitions keyed by id (mirrors `ItemCatalog`). Ships the "Return the Maguffin" fetch quest and the "Clear the Deck" bounty quest. |
| `Merchant` | `Merchant.cs` | A trading NPC's side of the shop ledger: name, credits, and stock `Inventory`. Built by `ShopkeeperRole` from the definition's `Credits`/`InitialItems`; not yet persisted. |
| `Trade` | `Trade.cs` | Engine-free buy/sell rules and execution between the party (`GameState.Credits`/`Inventory`) and a `Merchant`: buy at `Item.Value`, sell at half, quest/zero-value items untradable; returns user-facing result messages. |
| `ActiveStatusEffect`, `STATUSEFFECT` | `StatusEffect.cs` | Timed status effects (poison, sleep, confusion). |

## Not yet implemented

- Battle depth — defend/flee, status effects in battle, equipment-driven damage/defense, per-character powers, random encounters; see the [battle plan](plans/battle-system.md)
- Quest depth — stages/objectives in the journal, HUD notifications, rewards; see the [quest plan](plans/quest-system.md)
- Inventory depth — unequipping without a replacement (equipping over a slot swaps the old item back), drop-as-world-pickup, stat deltas before equipping, pickup persistence in world state, HUD pickup toast; see the [inventory plan](plans/inventory-system.md)
- NPC depth — Yarn Spinner dialogue, wander/patrol behaviors, NPC world-state persistence beyond party/quest data; see the [NPC plan](plans/npc-system.md)
- Shop depth — merchant stock/credit persistence across visits, buying/selling in quantities, per-merchant price modifiers, more interiors (houses) beyond the prototype shop
- Party depth — navmesh follower pathing, benched members beyond the active four, recruit/dismiss via scripted dialogue events (Yarn `<<recruit>>`), portraits in the Party tab; see the [party plan](plans/party-system.md)
- Save/load extras — autosave/quicksave, migrations, thumbnails, playtime; see the [save and load system plan](plans/save-load-system.md)
- Options menu
- Multiplayer (the server browser is debug-only UI)

## Known issues / cleanup candidates

- `Spawn.cs` handlers look inverted: `BodyExited` **adds** to the occupier list and `BodyEntered` **removes**, so `IsOccupied` reports the opposite of reality.
- `ActiveGamesList.AddActiveGameRow` calls `GD.Load` and discards the result.
- Escape (main menu) and Tab (in-game menu) still work while a dialogue is open, stacking menus over the dialogue box and fighting over the mouse mode.
- Pressing Escape on the main menu before any game has started hides the menu over an empty scene.
