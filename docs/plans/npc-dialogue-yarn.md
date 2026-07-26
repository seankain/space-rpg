# NPC Dialogue with Yarn — Implementation Plan

Goal: NPC conversations authored in [Yarn Spinner](https://yarnspinner.dev/) (`.yarn` scripts), presented in an in-game dialogue UI, with branching that reads and writes game state (NPC flags, quests, party, inventory).

Depends on: NPC plan Phase 1 (interaction plumbing) for triggering conversations.

## Where we are

Most of what this plan originally called for arrived by another road. The [dialogue-editor plan](dialogue-editor.md) built the dialogue UI, the "in dialogue" game mode, a serialized conversation format (`DialogueGraph`), a named effect/condition vocabulary, a validator, and an in-game editor — and migrated every intro NPC onto it. So Yarn is no longer needed as a *runtime*; it is needed as an **authoring format**, which is the part writers actually touch.

That is what Phase 1 below now does, and it is done:

- `Scripts/Dialogue/Yarn/` compiles Yarn source into the existing `DialogueGraph` (`YarnParser` → `YarnGraphCompiler`) and writes a graph back out as Yarn (`YarnGraphWriter`). `DialogueCatalog` loads `Resources/Dialogue/<id>.yarn` alongside `<id>.dialogue.json`; both end up as the same graph, so `DialogueRuntime`, `DialogueManager`, the effect/condition vocabulary, the validator, and the in-game editor are untouched.
- **Not the plugin.** The original recommendation was to vendor **YarnSpinner-Godot** and run its dialogue runner. That would mean a second dialogue runtime and a second conversation format living beside the one the game and its editor already use, and re-plumbing the UI/game-mode work that already exists. Compiling Yarn into the graph keeps one runtime and one editor, and it is the convergence the dialogue-editor plan asked for (Phase 5, "Yarn convergence"). The cost is that only a **subset** of Yarn is understood (see below) — the parser reports anything it can't express, with the source line, instead of mistranslating it.
- The supported syntax is a strict subset of real Yarn Spinner syntax, not a lookalike: `title:`/`---`/`===` nodes, `Speaker: line` lines, `-> option` groups with indented bodies, `<<jump>>`, `<<stop>>`, `<<if>>/<<elseif>>/<<else>>/<<endif>>`, custom commands, `//` comments, `#` line tags. Files stay readable by the upstream toolchain (the VS Code extension highlights and graphs them), which keeps the door open to swapping this parser for the real `YarnSpinner` NuGet compiler later without rewriting content.

## Conventions

- **One file, one conversation:** `Resources/Dialogue/<id>.yarn`, where the file stem is the conversation id — the same directory and id space as `<id>.dialogue.json`, discovered by the same scan. A conversation must live in *one* of the two forms; the catalog reports an id claimed by both.
- **The file's nodes are the conversation's nodes.** A `title:` becomes the graph node id, so links and node names in the editor match what the writer typed. The first node in the file is the entry, unless another carries an `entry: true` header.
- **`$npc` speaks as the NPC** playing the conversation (`DialogueGraph.SpeakerToken`), so a script isn't bound to one character. A line with no `Speaker:` prefix is narration.
- **Commands are the `DialogueEffects` verbs** — `<<give_item 4 1>>`, `<<take_item 1>>`, `<<set_quest 1 Success>>`, `<<advance_quest 1>>`, `<<credits 50>>`, `<<recruit 2>>`, `<<start_battle>>`, `<<open_shop>>`. A command above a line runs when that line shows; at the end of a block it runs on the line it was written under; at the top of an option's body it runs when that option is picked.
- **`<<if>>` takes one `DialogueConditions` call** — `has_item(1)`, `quest_state(1, "Success")`, `npc_defeated("intro.vex")`, `party_has_room()`. An `<<if>>` chain compiles to a router node; a `<<if>>` trailing an option gates that choice.
- **No Yarn variables or expressions.** `$variables`, comparisons, `and`/`or`/`not`, `<<set>>`, `<<declare>>`, `<<wait>>` are rejected with a line-numbered message. Dialogue reads game state through the condition vocabulary; anything it can't ask for is a missing condition to add, not a Yarn expression to write. (Phase 3 is where a general world-flag store would land.)
- **Editing preserves the format.** The in-game editor tracks which form a conversation came from (`DialogueGraph.SourceFormat`) and `dialogue save` writes it back the same way, so editing a `.yarn` conversation in-game doesn't leave a rival `.json` with the same id. `dialogue list` shows each conversation's format.
- **Exported builds:** `.yarn` (like `.dialogue.json`) is a plain text file, not a Godot resource — an export preset's "non-resource files to export" filter needs `*.yarn` alongside `*.json` for a packaged build to find conversations.

---

## Phase 1 — Yarn as the authoring format *(done)*

1. `Scripts/Dialogue/Yarn/YarnParser.cs` — engine-free parse of the syntax subset into a statement tree, with line numbers on everything.
2. `Scripts/Dialogue/Yarn/YarnGraphCompiler.cs` — statements → `DialogueGraph`: lines to nodes, option groups to the preceding line's choices, commands to `EffectRef`s, `<<if>>` chains to router nodes, `<<jump>>`/`<<stop>>` to links. Problems are collected, not thrown: a flawed script still loads (and shows the same trouble in the editor's validator) rather than disappearing.
3. `Scripts/Dialogue/Yarn/YarnGraphWriter.cs` — `DialogueGraph` → Yarn source, one node per graph node with explicit `<<jump>>`s, so ids survive and the write→parse round trip is exact. Used by the editor's save path and to convert a `.dialogue.json` conversation to Yarn.
4. `DialogueCatalog` loads `*.yarn`; `DialogueEditing.Save` writes back in the source format; `dialogue list` reports it.
5. `Resources/Dialogue/example.greeter.yarn` — the sample conversation, converted, authored in the idiomatic nested style (an option's reply indented under it).
6. **Tests** (`Tests/YarnDialogueTests.cs`): the mapping of every construct; the problems reported for unknown verbs, unsupported Yarn, and broken structure; a compiled conversation played through `DialogueRuntime` against a live `GameState`; and a round-trip guard over **every committed conversation** in both formats — a `.dialogue.json` written to Yarn and recompiled must be the same graph.

**Done when:** a `.yarn` file in `Resources/Dialogue` plays in-game through the normal dialogue box, with choices, gating, and effects, and the in-game editor can open, edit, and save it as Yarn. ✅

## Phase 2 — Move the authored conversations to Yarn

1. Convert the six intro conversations (`intro.dockmaster_hale`, `intro.chief_marlow`, `intro.chief_marlow_recruit`, `intro.rig`, `intro.vex`, `intro.shopkeeper`) from `.dialogue.json` to `.yarn`, one at a time: write the graph out with `YarnGraphWriter`, then tidy it into the nested style by hand where it reads better. The round-trip test is the safety net; `Tests/DialogueMigrationTests.cs` (which scans `*.dialogue.json`) grows a `.yarn` equivalent as files move.
2. Play each converted NPC through every branch — offer/decline, in-progress, turn-in, refusal-to-battle, recruit, quest-gated recruit — the same regression pass the data migration used.
3. Decide whether `.dialogue.json` stays a supported input (it is the editor's native output for brand-new conversations) or becomes legacy once the content is Yarn. Recommendation: keep both; the editor writes JSON for a `dialogue new`, and an author converts to Yarn when the conversation is worth writing prose in.

**Done when:** the intro NPCs' conversations are `.yarn` files, behave identically in game, and the dialogue editor still opens and saves them.

## Phase 3 — Game-state bridge (world flags)

The condition vocabulary covers quests, inventory, defeated NPCs, and party room. What it can't express is the ad-hoc `$met_dockmaster` flag a writer reaches for constantly.

1. A **world-flag store** in `GameState` (a `Dictionary<string, string>` in the world-state bucket, alongside `DefeatedNpcs`), with a `SaveVersion` bump and migration, so flags survive save/load.
2. New vocabulary over it: a `flag("met_hale")` / `flag("met_hale", "2")` condition and a `<<set_flag met_hale true>>` effect — named verbs like every other, so the editor's dropdowns and the validator pick them up for free.
3. Read-only facts worth exposing as conditions once something needs them: party size, leader name, and Charisma (the build plan's dialogue modifier) for stat-gated options.

**Done when:** an NPC greets differently on second interaction, and after save/quit/load still remembers you.

## Phase 4 — Commands (dialogue acts on the world)

Most of this vocabulary already exists and is what Yarn commands compile to. What is left:

- `<<set_npc_flag NpcId Flag Value>>` (NPC plan Phase 4) — likely subsumed by Phase 3's flag store.
- `<<play_anim ...>>` / camera or emote niceties.
- Keep command handlers thin — they call the owning system's API, no game logic inside dialogue glue (`DialogueEffects` already holds this line).

**Done when:** a full "fetch" loop authored purely in Yarn works: NPC starts a quest, notices the fetched item, takes it, and rewards the player. (The Maguffin loop already does this from data; the acceptance is doing it from a `.yarn` file, which Phase 2 delivers.)

## Phase 5 — Production concerns (defer until content ramps)

- **Voice/portraits:** speaker portrait slot in the dialogue box keyed by character id.
- **Localization:** Yarn's line-ID support (`#line:...`) is parsed and currently dropped. Adopting line ids means externalizing node text to keys — painful to retrofit, so decide before writing lots of content (dialogue-editor plan Phase 5 item 3 is the same decision from the editor's side).
- **Writer workflow:** document the file-per-conversation and node-naming conventions in a writers' README; the Yarn Spinner VS Code extension already gives syntax highlighting and a graph preview of these files.
- **Upstream compiler:** if the subset starts to chafe, swap `YarnParser` for the real `YarnSpinner` NuGet compiler and keep `YarnGraphCompiler` as the mapping onto the graph. The content doesn't move.

## Decisions settled

| Decision | Where it landed |
|----------|-----------------|
| Plugin vs core-library vs compile-to-graph | **Compile to the existing graph.** The game already has a dialogue runtime, UI, editor, and validator; the YarnSpinner-Godot plugin would have added a second runtime and a second format beside them. Yarn is the authoring layer, `DialogueGraph` is the runtime layer. |
| How much Yarn to support | A subset that maps cleanly onto the graph, with everything else reported by line number. Real Yarn syntax throughout, so files stay valid for the upstream tooling. |
| Where conversations live | `Resources/Dialogue/<id>.yarn`, the same directory and id space as `<id>.dialogue.json` — one conversation in one file, one catalog, one `dialogue list`. |
| Where dialogue state saves | In `GameState`, via the condition/effect vocabulary (quests, inventory, defeated NPCs today; a flag store in Phase 3) — never in a plugin's private storage, or saves won't capture it. |
| Dialogue camera | None for v1 (keep the gameplay camera); revisit after combat/camera work matures. |
