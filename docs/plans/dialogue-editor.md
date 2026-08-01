# Dialogue Editor — Implementation Plan

Goal: let an author edit NPC dialogue **trees inside the running game** and save them out to
be loaded by the normal game. This is requirement **3** of the in-game editor initiative
([in-game-editor.md](in-game-editor.md)) and the largest piece, because today dialogue is
*code*, not data — so the bulk of the work is moving dialogue into an editable, serializable
format first, then building the editor on top of it.

## Where we are

- The dialogue runtime is a tiny hand-authored tree: `DialogueLine` / `DialogueChoice`
  (`Scripts/Dialogue/Dialogue.cs`) — engine-free POCOs with `Speaker`, `Text`, an `OnShown`
  `Action`, an optional `List<DialogueChoice>`, and a `Next` link. `DialogueManager`
  (`Scripts/Dialogue/DialogueManager.cs`, autoload) walks one `DialogueLine` at a time in a
  code-built bottom box, advancing on Interact and rendering choices as buttons.
- **These trees are built in C#**, inside the NPC role classes (`Scripts/Npc/Roles/`:
  `QuestGiverRole`, `BountyGiverRole`, `ShopkeeperRole`, `RecruitRole`, `ChallengerRole`).
  Branching and side effects are expressed as C# lambdas: `OnShown`/`choice.Action` call into
  game systems (take an item, `SetQuestState`, `PartyManager.TryAddMember`,
  `DialogueActions.StartBattle`). There is **no serialized dialogue file anywhere** and no
  loader — the conversation only exists as executed code.
- A [Yarn Spinner plan](npc-dialogue-yarn.md) exists on paper (author dialogue in `.yarn`,
  bridge commands to game state) but is **not implemented**; the homegrown tree is explicitly
  the placeholder until then.
- The in-game editor console (`DevConsole`, command registry, editor-mode toggle, debug-build
  gating, `res://` save via `ResourceSaver`/file writes) is built in
  [in-game-editor.md](in-game-editor.md) and is the host for this editor's commands and the
  same save-to-repo workflow.

The hard constraint: a dialogue editor can only edit **data**. So Phase 1 introduces a
serialized dialogue format and a runtime that plays it, and migrates the existing
code-authored conversations onto it. Side effects (give item, start quest, start battle,
recruit) become **named, parameterized actions** referenced by data rather than inline
lambdas — otherwise an editor could never touch a branch that "does something."

## Conventions

- **Dialogue is data on disk.** One conversation per file: `Resources/Dialogue/<id>.dialogue.json`
  (JSON, `System.Text.Json`, matching the save system's serialization habit and the "directory
  is the content" convention `NpcDatabase`/`ChunkManager` already use). A `DialogueCatalog`
  discovers them by scanning the directory — no manifest.
- **Nodes and edges, not a linked object graph.** The serialized form is a flat list of
  dialogue nodes each with a stable string `Id`, `Speaker`, `Text`, an ordered list of
  choices, and a `Next` node id — links are ids, not object references. This is what an editor
  can safely rewire, and it round-trips through JSON without cycles-as-references problems.
- **Side effects are named actions.** Instead of C# lambdas, a node/choice references an
  `EffectId` (enum/string) plus string params, e.g. `give_item:4:2`, `set_quest:1:Success`,
  `start_battle`, `recruit:2`. A single engine-free `DialogueEffects` dispatcher maps an
  `EffectId`+params to the concrete game call (the current lambda bodies, relocated). The
  editor only ever picks from this fixed vocabulary, so authored data can't invent behavior.
- **Conditions are named too.** Branch visibility (a quest-gated choice, an already-recruited
  veto) is a named `ConditionId`+params evaluated against `GameState`, so the data can express
  the gating the role code does today without embedding code.
- **Roles reference a conversation by id.** An `NpcRole`/`NpcDefinition` names a dialogue id
  instead of building a tree in code; `Npc` asks `DialogueCatalog` for the tree and hands it to
  `DialogueManager`. The composition model (several roles → a choice menu) is preserved by a
  role contributing an entry node id.
- **Editor is dev-only and commits its output**, exactly like NPC placement: the save writes
  `res://Resources/Dialogue/*.json`, gated to debug builds, committed to the repo. Same
  read-only-`res://` caveat as the rest of the in-game editor.
- **This is the placeholder format, still.** The Yarn plan remains the eventual target; keeping
  effects/conditions as a small named vocabulary means a future Yarn bridge maps its commands
  onto the same `DialogueEffects`, not a rewrite of every role.

---

## Phase 1 — Serializable dialogue data model + runtime *(foundation, no editor yet)*

1. `Scripts/Dialogue/DialogueGraph.cs` (engine-free): `DialogueGraph { string Id; List<DialogueNode> Nodes; string EntryNodeId }`,
   `DialogueNode { string Id, Speaker, Text; List<DialogueChoiceData> Choices; string NextNodeId; EffectRef OnShownEffect }`,
   `DialogueChoiceData { string Label; EffectRef Effect; string NextNodeId; ConditionRef Visible }`,
   and `EffectRef`/`ConditionRef { string Id; string[] Args }`. JSON-serializable with
   `System.Text.Json`.
2. `Scripts/Dialogue/DialogueEffects.cs` and `DialogueConditions.cs`: the fixed vocabulary.
   Effects dispatch to game systems (`give_item`, `take_item`, `set_quest`, `advance_quest`,
   `recruit`, `start_battle`, `open_shop`, `credits`); conditions evaluate against `GameState`
   (`quest_state`, `has_item`, `npc_defeated`, `party_has_room`). Each is a small case that
   relocates an existing role lambda body. Effects needing a live `Npc`/scene take it via a
   context object passed at play time.
3. `Scripts/Dialogue/DialogueRuntime.cs`: converts a `DialogueGraph` (+ a play context:
   current `Npc`, `GameState`) into the existing `DialogueLine`/`DialogueChoice` runtime tree
   — resolving node ids to links, filtering choices by condition, and wiring `OnShown`/`Action`
   to `DialogueEffects.Run`. `DialogueManager` is unchanged; it still plays `DialogueLine`s.
4. `Scripts/Dialogue/DialogueCatalog.cs`: scan `res://Resources/Dialogue/` for `*.dialogue.json`,
   parse to `DialogueGraph`, cache by id (the `ItemCatalog`/`NpcDatabase` pattern, with an
   `Invalidate()` hook for the editor).
5. **Tests** (`Tests/DialogueGraphTests.cs`): JSON round-trip of a graph; `DialogueRuntime`
   link resolution (node ids → correct `Next`, choices in order); condition filtering hides a
   gated choice; effect dispatch invokes the right `GameState` mutation; a dangling `NextNodeId`
   is reported, not crashed.

**Done when:** a `DialogueGraph` authored as JSON loads through `DialogueCatalog`, converts to
the runtime tree, and plays in-game identically to a code-built tree, with a gated choice
appearing/disappearing by quest state and a `give_item`/`set_quest` effect firing — all covered
by engine-free tests.

## Phase 2 — Migrate existing conversations onto the data model

1. For each role in `Scripts/Npc/Roles/`, extract its code-built tree into a
   `Resources/Dialogue/<npc-or-role>.dialogue.json` file, translating each inline lambda to the
   matching `EffectRef`/`ConditionRef` from Phase 1's vocabulary (add any missing verb the
   conversations need — e.g. the Maguffin refusal → `start_battle`, Marlow's quest-gated recruit
   → a `quest_state` condition).
2. Change roles/`NpcDefinition` to reference a dialogue id and ask `DialogueCatalog` +
   `DialogueRuntime` for the tree instead of constructing `DialogueLine`s in C#. Preserve the
   composition behavior (multi-role choice menu) by having each role contribute its entry node.
3. Delete the now-dead tree-building code from the roles once parity is confirmed; keep
   `DialogueActions.StartBattle` reachable via the `start_battle` effect.
4. **Regression check:** play each of the four intro NPCs (Rig, Hale, Vex, Marlow) through
   every branch — offer/decline, in-progress, turn-in, refusal-to-battle, recruit,
   quest-gated recruit — and confirm identical behavior to before the migration.

**Done when:** all existing NPC dialogue is authored as committed `.dialogue.json` data, the
roles no longer build trees in code, and every intro conversation branch behaves exactly as it
did before — the game reads dialogue entirely from data.

## Phase 3 — Read-only dialogue viewer in editor mode

1. A `dialogue open <id>` console command (registered with the `DevConsole` registry) opens a
   dialogue-editor panel (a new `CanvasLayer` UI, code-built like the other editor UI) showing
   the loaded graph.
2. Render the graph as a **node list + detail view** to start (simpler and more robust than a
   full node-graph canvas): a left list of nodes, a right panel showing the selected node's
   speaker/text, its choices (label, effect, target node), and `Next`. Selecting a choice's
   target navigates to that node. Read-only in this phase.
3. `dialogue list` prints known dialogue ids; the panel has a picker too. Closing the panel
   restores play the same way the console/editor mode does.

**Done when:** in a debug session the author can open any conversation and walk its full node/
choice structure in a panel, seeing speaker, text, effects, conditions, and link targets,
without changing anything.

## Phase 4 — Editing and saving dialogue

1. Make the detail view editable: rename/rewrite speaker and text; add/remove/reorder choices;
   set each choice's target node (dropdown of existing node ids) and its effect/condition from
   the fixed vocabulary (dropdowns of `EffectId`/`ConditionId` + arg fields); set a node's
   `Next` and `OnShownEffect`. Add/delete nodes; pick the entry node.
2. **Validation before save** (engine-free, tested): no dangling link targets, entry node
   exists, no orphan nodes unreachable from entry (warn, don't block), effect/condition args
   parse against their vocabulary. Show problems inline in the panel.
3. `dialogue save [id]` writes the edited `DialogueGraph` to
   `res://Resources/Dialogue/<id>.yarn` (`YarnGraphWriter`, since the Yarn convergence below
   landed — it wrote `<id>.dialogue.json` via `System.Text.Json` when this phase shipped),
   debug-build gated, creating a new file for a new id — the same commit-to-repo workflow as
   NPC placement. Then `DialogueCatalog.Invalidate()` so the next conversation plays the
   edited version live.
4. `dialogue new <id>` seeds an empty single-node graph to author a conversation from scratch,
   and `dialogue assign <npcId> <dialogueId>` points an NPC/role at it (updating and re-saving
   the `NpcDefinition` `.tres` through the Phase-3 NPC save path).
5. **Tests** (`Tests/DialogueEditTests.cs`): graph edits (add/remove/relink node and choice)
   preserve a valid graph; the validator flags a dangling link, a missing entry, a bad effect
   arg; save→reload round-trips an edited graph byte-stably enough to diff cleanly in a PR.

**Done when:** the author can edit a conversation's text, choices, links, effects, and
conditions in-game, save it to a committed `.json`, and immediately replay the NPC to see the
change — and can author a brand-new conversation and assign it to an NPC, all in a debug
session, with the edits landing as reviewable repo diffs.

## Phase 5 — Polish and stretch

1. **Graph canvas view:** upgrade the node-list editor to a draggable node-graph (Godot
   `GraphEdit`) with visible edges, laid out and saved with optional editor-only node positions
   (kept out of the gameplay data or in a sibling `*.layout.json` so play data stays minimal).
2. **Live preview:** a "play from here" button that runs the current (unsaved) graph through
   `DialogueManager` against the running `GameState` for instant iteration.
3. **Localization-ready text:** optionally externalize node `Text` to string keys once a
   localization system exists, so the editor edits keys + a table.
4. **Yarn convergence** *(done — [Yarn plan](npc-dialogue-yarn.md) Phase 1)*: `.yarn` files compile
   into the same `DialogueGraph` this editor edits, with Yarn commands mapping onto the same
   `DialogueEffects` vocabulary and `<<if>>` onto `DialogueConditions`. The editor opens a Yarn
   conversation like any other and saves it back as Yarn, so the two formats are interoperable
   rather than competing.

## Decisions to settle early

| Decision | Recommendation |
|----------|----------------|
| Edit code or data | Data. An editor can't edit compiled lambdas; the prerequisite work is moving dialogue into a serialized graph and playing *that*. Phases 1–2 are that migration; the editor (3–4) sits on top. |
| Serialized format | Per-conversation `Resources/Dialogue/<id>.dialogue.json`, discovered by directory scan — consistent with the save system's JSON and the manifest-free catalogs already in the codebase. |
| Node links | Store target **node ids**, not object references — the only shape an editor can safely rewire and that JSON round-trips cleanly. `DialogueRuntime` resolves ids to the existing `DialogueLine.Next` links at play time. |
| Side effects | A fixed, named `DialogueEffects` vocabulary (relocated from the current role lambdas) referenced by data. The editor picks from the list; authored data can never invent new behavior. Same for `DialogueConditions` gating. |
| Runtime reuse | Keep `DialogueManager` and the `DialogueLine`/`DialogueChoice` runtime untouched; add a `DialogueRuntime` that compiles a `DialogueGraph` into that existing tree. Minimal blast radius, and the migration can proceed conversation-by-conversation. |
| First editor UI | A node-list + detail panel before a full `GraphEdit` canvas — faster to build, easier to validate, and enough to edit real conversations; the canvas is Phase 5 polish. |
| Relationship to Yarn | This is still the placeholder format. Keeping effects/conditions as a small named set means the eventual Yarn bridge targets the same dispatcher, not a from-scratch rewrite. |
| Dev-only + persistence | Editing/saving is debug-build gated and writes committed `res://` JSON, matching the NPC-placement and map-baker workflows; release builds don't edit content. |
