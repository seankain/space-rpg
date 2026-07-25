# In-Game Editor — Implementation Plan

Goal: a developer-facing editor mode that runs **inside the live game**, driven by a
tilde-activated console. From the console the author can toggle editor mode, place NPCs
into chunks and save them as loadable content, and mutate the running save — grant items,
start quests, and advance them to a chosen stage — for fast iteration and QA.

This plan covers requirements **1** (console + NPC placement/save) and **2** (item and quest
commands). Requirement **3** (the dialogue editor) is a larger, separate effort in
[dialogue-editor.md](dialogue-editor.md); it reuses the console framework built here.

## Where we are

- Global input is handled in `LevelManager._UnhandledInput` (`Scripts/LevelManager.cs`):
  Escape toggles the main menu, Tab toggles the in-game menu, both flipping
  `Input.MouseMode`. There is no console and no dedicated editor mode.
- Gameplay input is suppressed cooperatively: `Player`, `Pickup`, and `Npc` all check
  `DialogueManager.IsDialogueActive` before acting. Editor mode should gate the same way.
- NPCs are **data-driven**: one `NpcDefinition` (`Scripts/Data/NpcDefinition.cs`) `.tres`
  per NPC under `Resources/Npcs/<Area>/`, discovered by a recursive scan in `NpcDatabase`
  (`Scripts/Data/NpcDatabase.cs`) — no manifest, same rationale as `ChunkManager.DiscoverChunks`.
  A definition records `NpcId`, `DisplayName`, `SpawnScenePath`, `ChunkCoords`,
  `LocalPosition`, `RotationDegreesY`, `Roles`, `Behavior`, and a `Rig` scene.
- `NpcSpawner.Spawn(definition, parent)` (`Scripts/Npc/NpcSpawner.cs`) instantiates the
  shared `Scenes/Npc.tscn`, primes it with a definition, and parents it at
  `LocalPosition`/`RotationDegreesY`. `ChunkManager.AddChunk` calls it for every
  `NpcDatabase.ForChunk(levelPath, coord)` when a chunk streams in.
- `NpcDatabase.Index` is a **static cached** `NpcIndex` built on first use. Nothing
  invalidates it today — a newly authored `.tres` needs a fresh index to appear.
- Items live in `ItemCatalog` (ids 1–5), the party inventory is `GameState.Inventory`
  (`Inventory.Add(itemId, qty)`), credits are `GameState.Credits`.
- Quests live in `QuestCatalog` (ids `ReturnTheMaguffinId`, `ClearTheDeckId`); per-save
  progress is `GameState.Quests` with `GetQuestState`/`SetQuestState` and a
  `QuestProgress.CurrentStageNumber` field that nothing writes yet. `QuestStage` exists as
  a bare class but no quest declares stages.
- The running save is `SaveManager.Instance.CurrentState` (a `GameState`); `SaveManager`
  is an autoload, as are `DialogueManager` and `BattleManager`.
- Precedent for editor-only tooling: the world-map bake tool (`addons/map_baker/`,
  `Scripts/Editor/`) runs in the Godot editor and commits its output (`Resources/Maps/**`)
  to the repo. The in-game editor is the runtime analogue.

## Conventions

- **Dev-only, and honest about it.** Editor mode and every mutating command are gated to
  debug builds (`OS.HasFeature("editor") || OS.IsDebugBuild()`). Writing authored content
  back to `res://` only works in the editor/debug player — `res://` is read-only in an
  exported release build — so the *save* commands are a development-time authoring tool,
  exactly like the map baker. In a release build the console either does not open or
  refuses mutating commands with a clear message.
- **One console, a command registry.** Commands are not a growing `switch`. Each command is
  a small object (name, usage string, `Execute(string[] args) → CommandResult`) registered
  in a dictionary, so features 1 and 2 (and later the dialogue editor) add commands without
  touching the console core. The registry and the arg parser are engine-free and unit-tested.
- **Source of truth = the same files the game already reads.** Placing and saving an NPC
  writes an `NpcDefinition` `.tres` under `Resources/Npcs/<Area>/` — the exact format
  `NpcDatabase` scans — so authored NPCs load through the normal `ChunkManager` path with no
  parallel data channel. Item/quest commands mutate `GameState`, the same object the save
  system persists, so `Save Game` captures editor changes for free.
- **Editor state is not gameplay state.** Toggling editor mode does not touch the save;
  it changes input handling and shows editor UI. The only persistent output is content the
  author explicitly saves (a `.tres`) or a game the author explicitly saves through the
  normal menu.
- **Coordinate math is shared, never re-derived.** World→chunk conversion reuses
  `ChunkManager.ToChunkCoord` / `MapProjection`; the `LocalPosition` an NPC gets is
  `worldPos − chunkCoord·64`, computed in one helper so the console and the streamer agree.

---

## Phase 1 — Console window and command framework *(foundation)*

1. Add a `ToggleConsole` input action to `project.godot` bound to the tilde/backtick key
   (`Key.Quoteleft`, physical keycode `96`), mirroring the existing action blocks.
2. `Scripts/Editor/DevConsole.cs`: a new **autoload** `CanvasLayer` (high layer, above the
   dialogue box) built entirely in code like `DialogueManager` — a scrollback `RichTextLabel`
   output log and a single-line `LineEdit` input at the bottom. `ToggleConsole` shows/hides
   it and releases/recaptures the mouse; while visible it consumes input and echoes typed
   commands. In a non-debug build `_Ready` disables the toggle entirely.
3. `Scripts/Editor/Commands/ConsoleCommand.cs` + `CommandRegistry`: engine-free command
   contract (`Name`, `Usage`, `Summary`, `CommandResult Execute(string[] args, ...)`),
   a tokenizer that splits a raw line into a command name + args (respecting double-quoted
   arguments so display names with spaces work), and a registry that dispatches by name and
   returns `unknown command` for misses. `CommandResult` carries success + a message the
   console prints (green/red).
4. Built-in commands: `help` (lists registered commands + usage), `clear`, and `echo`.
   Register them at console `_Ready`.
5. **Tests** (`Tests/DevConsoleTests.cs`): the tokenizer (quotes, extra whitespace, empty
   line), registry dispatch (known/unknown/case-insensitivity), and `help`/`echo` output —
   all against the engine-free registry, no Godot node needed.

**Done when:** pressing tilde in a running debug game opens a console with a working input
line; `help` lists commands, `echo hi` prints `hi`, an unknown command reports the error;
the tests cover parsing and dispatch; a release build never opens the console.

## Phase 2 — Editor mode toggle and placement cursor

1. `editor` command (alias `edit`) toggles a global editor mode held on `DevConsole`
   (`DevConsole.IsEditorActive`), printing the new state. Editor mode is orthogonal to the
   console being open — you turn it on from the console, then close the console and act.
2. Gate gameplay while editor mode is active the same cooperative way dialogue does: add
   `DevConsole.IsEditorActive` to the guards in `Player`, `Pickup`, and `Npc` (suppress
   movement, jump, pickups, and NPC interaction) so the author can move a free camera / point
   without triggering play.
3. A minimal **placement cursor**: while editor mode is on, a ray from the camera through the
   screen center (or mouse) hits world geometry and a translucent marker shows where a
   `place` command would drop an NPC. A `here` token in placement commands resolves to this
   cursor's world position; explicit coordinates remain available for precision.
4. On-screen editor HUD strip (small `Label`) showing `EDITOR MODE` and the cursor's world
   position + resolved chunk coordinate, so the author sees exactly what `here` means.

**Done when:** `editor` toggles a visible editor-mode indicator, gameplay input is suppressed
while it is on, and a cursor marker tracks a valid ground point with its world + chunk
coordinate shown; toggling back off restores normal play.

## Phase 3 — Place an NPC and save it as loadable content *(requirement 1 core)*

1. `spawn <npcId>` — instantiate an existing `NpcDefinition` (via `NpcDatabase.Get`) at the
   placement cursor for a throwaway preview in the current session (not saved). Reuses
   `NpcSpawner.Spawn` into the current chunk node so behavior/roles run normally.
2. `place <npcId> <displayName> [rig] [here | <x> <y> <z>]` — author a **new** persistent NPC:
   - Resolve the target position (cursor `here` or explicit world coords), derive
     `ChunkCoords` via `ChunkManager.ToChunkCoord` and `LocalPosition` = world − chunk·64
     through the shared helper, and read the current level's scene path for `SpawnScenePath`.
   - Build an `NpcDefinition` in memory (id, display name, transform, optional `Rig` resolved
     by name from `Scenes/Characters/Rigs/`, default `Stationary` behavior, no roles yet),
     spawn it immediately for preview, and hold it as the "pending placement."
   - Validate the id is unique against `NpcDatabase` and non-empty; reject duplicates.
3. `savenpc` — persist the pending placement: `ResourceSaver.Save` the `NpcDefinition` to
   `res://Resources/Npcs/<Area>/<npcId>.tres` (area from the level scene basename), creating
   the directory if needed. Guard on debug build; on a release build, refuse with a message
   pointing at the map-baker precedent (author in the editor player, commit the `.tres`).
4. Invalidate the cached `NpcDatabase` index (add a `Reload()`/`Invalidate()` static hook)
   so the new `.tres` is discoverable in-session, and confirm the saved path in the console.
5. Companion commands: `nudge <dx> <dy> <dz>` and `rotate <deg>` to fine-tune the pending
   placement before saving; `list npcs [area]` to print known ids; `cancelplace` to discard.
6. **Tests** (`Tests/NpcPlacementTests.cs`): the world→(chunk, local) split helper (round-trip
   across chunk borders and negative coords) and the area-from-scene-path derivation — both
   engine-free. (`ResourceSaver` I/O is exercised manually in the editor, like the baker.)

**Done when:** in a debug session the author can `place` a new NPC at the cursor, see it in
the world, `savenpc`, and — after leaving and re-entering the chunk (or reloading the level) —
the NPC streams back in through the normal `ChunkManager`/`NpcDatabase` path from the committed
`.tres`, at the right chunk and local position.

## Phase 4 — Item and credit commands *(requirement 2, items)*

1. `give <itemId> [qty]` — add to `SaveManager.Instance.CurrentState.Inventory` via
   `Inventory.Add`, honoring stack caps; default qty 1. Resolve and validate the id against
   `ItemCatalog`, and print the item name + resulting stack count.
2. `giveitem "<name>" [qty]` — same, resolving by `ItemCatalog` name (quoted) for convenience;
   ambiguous/unknown names report the error.
3. `takeitem <itemId> [qty]`, `credits <amount>` (set) / `credits +<amount>` (add) against
   `GameState.Credits`, and `items` to list the catalog (id, name, category).
4. Feedback: since the inventory HUD only refreshes on tab open, print the change to the
   console; note in-code that an open Inventory tab won't live-update until refreshed (out of
   scope here).
5. **Tests** (`Tests/EditorItemCommandTests.cs`): `give`/`takeitem`/`credits` executed against
   a fabricated `GameState` (engine-free) — correct stacks, cap clamping, unknown-id rejection,
   credit add-vs-set.

**Done when:** `give 4 2` puts two Medkits in the party inventory (visible on the Inventory
tab), `credits +500` and `credits 1000` behave as add vs. set, unknown ids are rejected, and
the command logic is unit-tested.

## Phase 5 — Quest commands *(requirement 2, quests)*

1. `quest start <questId>` — set the quest to `InProgress` via `GameState.SetQuestState`
   (creating the `QuestProgress` if absent); validate against `QuestCatalog`.
2. `quest set <questId> <state>` — set any `QUESTSUCCESSSTATE` (`unstarted|inprogress|success|failed`,
   parsed case-insensitively) for reaching turn-in / failure branches directly.
3. `quest stage <questId> <n>` — write `QuestProgress.CurrentStageNumber` (the first thing to
   populate that field), and `quest advance <questId>` to increment it. Because no quest
   declares `QuestStage`s yet, this is deliberately unvalidated against a stage list; note the
   follow-up in the quest plan (`quest-system.md`) to validate `n` once stages exist.
4. `quests` — print each `QuestCatalog` quest with its current state and stage from the save,
   so the author can see what a branch expects.
5. **Tests** (`Tests/EditorQuestCommandTests.cs`): `quest start/set/stage/advance` against a
   fabricated `GameState` — state transitions, stage writes, string→enum parsing, unknown-id
   and bad-state rejection.

**Done when:** `quest start 1` makes "Return the Maguffin" appear in the Quest log as in
progress, `quest set 1 success` and `quest stage 1 2` land the expected state/stage, `quests`
prints live progress, and the command logic is unit-tested.

## Phase 6 — Polish and ergonomics

1. Command history (Up/Down in the input line) and Tab-completion of command names + known
   ids from the catalogs/`NpcDatabase`.
2. A `save` command wrapping `SaveManager` capture so the author can snapshot editor changes
   without opening the menu, plus `goto <x> <y> <z>` / `goto <npcId>` to teleport the player
   for QA.
3. Scriptable setup: `exec <file>` runs a newline-delimited list of console commands from a
   `res://` text file — reproducible test fixtures ("give the party the mid-game loadout").
4. Guard rails audit: confirm every mutating command is debug-gated and that the console can
   never open in a shipped build; a smoke test asserts the release gate.

## Decisions to settle early

| Decision | Recommendation |
|----------|----------------|
| Where does the console live | A new autoload `CanvasLayer` (`DevConsole`), like `DialogueManager` — always present, independent of which level/menu is up, single input owner for tilde. |
| Command shape | A registry of small command objects, not a `switch`. Keeps the console core closed and lets each feature (and the dialogue editor) register its own verbs; the registry + parser are engine-free and testable. |
| Saving NPCs at runtime | `ResourceSaver.Save` a real `NpcDefinition` `.tres` into `Resources/Npcs/<Area>/`, debug-build only, committed to the repo — the map-baker model. No custom runtime data channel; authored NPCs load through the existing `NpcDatabase`/`ChunkManager` path. |
| Release-build safety | Editor mode and all mutating commands are gated to `OS.IsDebugBuild()`/`OS.HasFeature("editor")`; a release build refuses them with a clear message. Prevents shipping a cheat/edit surface. |
| Refreshing the NPC index | Add an explicit `NpcDatabase.Invalidate()` and call it after `savenpc`, rather than reloading every frame. Cheap, and keeps the normal cached-once behavior for gameplay. |
| Item/quest persistence | Mutate `GameState` directly; the existing save system captures it. No new save fields, no version bump for the commands themselves. |
| Input suppression during editor mode | Reuse the cooperative `IsActive` guard pattern already used for dialogue (`Player`/`Pickup`/`Npc` check a static flag), not a new input-eating layer — consistent with the codebase and easy to reason about. |
