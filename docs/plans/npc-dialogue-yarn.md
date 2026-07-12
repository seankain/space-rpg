# NPC Dialogue with Yarn — Implementation Plan

Goal: NPC conversations authored in [Yarn Spinner](https://yarnspinner.dev/) (`.yarn` scripts), presented in an in-game dialogue UI, with branching that reads and writes game state (NPC flags, quests, party, inventory).

Depends on: NPC plan Phase 1 (interaction plumbing) for triggering conversations.

## Where we are

- Nothing dialogue-related exists yet. The `Interact` action and `Npc.tscn` shell are the only touchpoints.
- The project is C#-based, which matters for plugin choice: **YarnSpinner-Godot** (the community Godot port of Yarn Spinner) supports Godot 4 with C# and is the recommended integration; verify its current Godot 4.6 compatibility at adoption time. Fallback option if it stalls: run the core `YarnSpinner` .NET libraries (compiler + dialogue VM are plain NuGet packages) with a thin homegrown Godot presenter — more work up front but no engine-plugin dependency.

---

## Phase 1 — Plugin integration and hello world

1. Add YarnSpinner-Godot to `addons/` and its NuGet packages to `space-rpg.csproj`; commit the plugin (pin the version) so the project builds clean from clone.
2. Create `Dialogue/` for `.yarn` source files and wire the plugin's project settings (yarn project file, compiled program output).
3. Write a hello-world yarn node and run it from a debug key or autorun to prove compile + runtime work.

**Done when:** a `.yarn` file's lines print through the plugin's default dialogue runner in a running game.

## Phase 2 — Dialogue UI and NPC triggering

1. **DialogueBox UI** (`Scenes/Ui/DialogueBox.tscn`): speaker name, line text with typewriter reveal, continue prompt, and an option list for choices. Style minimally now; it's the template for all conversation UI.
2. **DialogueManager autoload:** starts a yarn node by name, routes lines/options to the DialogueBox, and owns the "in dialogue" game mode — player movement locked, camera unchanged, `Interact`/confirm advances lines (mirror how `LevelManager` handles menu mode).
3. NPC integration: `Npc.Interact()` calls `DialogueManager.StartNode(npc.DialogueNode)` using the entry node from the NPC definition (NPC plan Phase 1). Each NPC gets its own `.yarn` file with a `NpcName_Start` convention.

**Done when:** pressing E on an Intro-level NPC opens the dialogue box, plays a branching conversation with choices, and returns control cleanly.

## Phase 3 — Game-state bridge (variables)

1. Implement a **variable storage** backend for Yarn that persists into `GameState` — a `Dictionary<string, YarnValue>` in the world-state bucket (save plan Phase 5, alongside NPC flags). `$met_dockmaster`-style variables then survive save/load for free.
2. Expose read-only game facts as Yarn variables/functions: party members and size, leader name, quest states (`quest_state("id")`), inventory checks (`has_item(id)`), and Charisma (build plan's dialogue modifier hook) for stat-gated options.
3. Bump `SaveVersion` with a migration when the variable store lands.

**Done when:** an NPC greets differently on second interaction, and after save/quit/load still remembers you.

## Phase 4 — Commands (dialogue acts on the world)

Register Yarn **commands** so writers can drive gameplay from scripts:

- `<<give_item ItemId Quantity>>` / `<<take_item ItemId Quantity>>` (inventory plan)
- `<<start_quest QuestId>>` / `<<advance_quest QuestId StageNumber>>` (quest plan)
- `<<recruit NpcId>>` (party plan Phase 3)
- `<<set_npc_flag NpcId Flag Value>>` (NPC plan Phase 4)
- `<<play_anim ...>>` / camera or emote niceties as needed

Keep command handlers thin — they call the owning system's API, no game logic inside dialogue glue.

**Done when:** a full "fetch" loop authored purely in Yarn works: NPC starts a quest, notices the fetched item, takes it, and rewards the player.

## Phase 5 — Production concerns (defer until content ramps)

- **Voice/portraits:** speaker portrait slot in DialogueBox keyed by character id.
- **Localization:** Yarn Spinner has first-class line-ID/localization support — adopt line IDs before writing lots of content, painful to retrofit.
- **Writer workflow:** document the node-naming and file-per-NPC conventions; consider the Yarn Spinner VS Code extension for graph preview.

## Decisions to settle early

| Decision | Recommendation |
|----------|----------------|
| Plugin vs core-library integration | Try YarnSpinner-Godot first (C# support, maintained); keep the core-library fallback in mind — the `.yarn` content is portable either way, so scripts written now aren't at risk. |
| Where dialogue state saves | In `GameState` via the Yarn variable-storage bridge — never in plugin-private storage, or saves won't capture it. |
| Dialogue camera | None for v1 (keep gameplay camera); revisit after combat/camera work matures. |
