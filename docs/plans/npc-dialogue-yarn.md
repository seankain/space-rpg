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
- **Commands are the `DialogueEffects` verbs** — `<<give_item 4 1>>`, `<<take_item 1>>`, `<<set_quest 1 Success>>`, `<<advance_quest 1>>`, `<<credits 50>>`, `<<recruit 2>>`, `<<start_battle>>`, `<<open_shop>>`, `<<set_flag met_hale>>`, `<<play_anim Cheering>>`. A command above a line runs when that line shows; at the end of a block it runs on the line it was written under; at the top of an option's body it runs when that option is picked.
- **`<<if>>` takes one `DialogueConditions` call** — `has_item(1)`, `quest_state(1, "Success")`, `npc_defeated("intro.vex")`, `party_has_room()`, `flag("met_hale")`, `party_size(2)`, `stat("Charisma", 12)`. An `<<if>>` chain compiles to a router node; a `<<if>>` trailing an option gates that choice.
- **No Yarn variables or expressions.** `$variables`, comparisons, `and`/`or`/`not`, `<<set>>`, `<<declare>>`, `<<wait>>` are rejected with a line-numbered message. Dialogue reads and writes ad-hoc state through `flag()`/`<<set_flag>>` (Phase 3) and asks about the rest through the condition vocabulary; anything it can't ask for is a missing condition to add, not a Yarn expression to write.
- **The editor writes Yarn.** `dialogue save` always writes `<id>.yarn` — Yarn is *the* authoring format, and the in-game editor is one of the two ways content gets written, so it has no business producing a second one. A legacy `.dialogue.json` still loads; saving it **converts** it, writing the `.yarn` beside it and deleting the JSON it replaces (`DialogueGraph.SourcePath`), because a conversation lives in one file and a leftover would claim the same id. A rival `.json` the conversation was *not* loaded from is reported rather than deleted, and so is a rename (`dialogue save <other-id>`), which writes a second conversation and leaves the first where it is. `dialogue list` shows each conversation's format, so what is left to convert is visible.
- **Exported builds:** `.yarn` (like `.dialogue.json`) is a plain text file, not a Godot resource — an export preset's "non-resource files to export" filter needs `*.yarn` alongside `*.json` for a packaged build to find conversations.

---

## Phase 1 — Yarn as the authoring format *(done)*

1. `Scripts/Dialogue/Yarn/YarnParser.cs` — engine-free parse of the syntax subset into a statement tree, with line numbers on everything.
2. `Scripts/Dialogue/Yarn/YarnGraphCompiler.cs` — statements → `DialogueGraph`: lines to nodes, option groups to the preceding line's choices, commands to `EffectRef`s, `<<if>>` chains to router nodes, `<<jump>>`/`<<stop>>` to links. Problems are collected, not thrown: a flawed script still loads (and shows the same trouble in the editor's validator) rather than disappearing.
3. `Scripts/Dialogue/Yarn/YarnGraphWriter.cs` — `DialogueGraph` → Yarn source, one node per graph node with explicit `<<jump>>`s, so ids survive and the write→parse round trip is exact. Used by the editor's save path and to convert a `.dialogue.json` conversation to Yarn.
4. `DialogueCatalog` loads `*.yarn`; `DialogueEditing.Save` writes Yarn (converting a `.dialogue.json` conversation and deleting it); `dialogue list` reports each conversation's format.
5. `Resources/Dialogue/example.greeter.yarn` — the sample conversation, converted, authored in the idiomatic nested style (an option's reply indented under it).
6. **Tests** (`Tests/YarnDialogueTests.cs`): the mapping of every construct; the problems reported for unknown verbs, unsupported Yarn, and broken structure; a compiled conversation played through `DialogueRuntime` against a live `GameState`; and a round-trip guard over **every committed conversation** — written back out and recompiled, and crossed through JSON and back, it must be the same graph.

**Done when:** a `.yarn` file in `Resources/Dialogue` plays in-game through the normal dialogue box, with choices, gating, and effects, and the in-game editor can open, edit, and save it as Yarn. ✅

## Phase 2 — Move the authored conversations to Yarn *(done)*

1. All six intro conversations (`intro.dockmaster_hale`, `intro.chief_marlow`, `intro.chief_marlow_recruit`, `intro.rig`, `intro.vex`, `intro.shopkeeper`) are now `.yarn`; the `.dialogue.json` files are gone. Each was hand-written in the nested style — an option's reply indented under it rather than jumping to a node of its own — so the scripts read as dialogue. Nodes that more than one branch links to (`declined`, `complete`, `turnin`) keep their own `title:`, because they are real destinations; the ids of inlined replies are generated (`offer__2`) and no longer show under their old names in the editor's node list. Nothing outside the file referenced them — a role names a conversation id, never a node id.
2. **Equivalence was checked mechanically, not by eye.** A throwaway harness compared each new `.yarn` graph against the `.dialogue.json` it replaced as a bisimulation from the entry node — same speakers, text, effects, conditions, choice order and branch order, up to node renaming — and was itself checked by mutating a script and confirming it failed. That is what made deleting the JSON safe in one commit.
3. The branch-by-branch regression pass lives on as `Tests/IntroDialogueBranchTests.cs`, which plays the committed conversations through `DialogueRuntime` against real `GameState`s: offer/decline, the in-progress reminder, turn-in (including picking the cube up before taking the quest), refusal-to-battle, a beaten Hale backing down, the bounty paid before *and* after Vex falls, both recruits, a full party, Vex's despawning fight, and the shop hand-off. `Tests/DialogueMigrationTests.cs` now scans both formats.
4. `.dialogue.json` stays a supported *input* — it loads, and the editor opens it — but nothing writes it any more: `dialogue save` writes `.yarn` for every conversation, including a `dialogue new` one, and converting a JSON conversation deletes the file it replaces. `Tests/YarnDialogueTests.cs` guards that a conversation crosses between the two formats unchanged in both directions, which is what makes that conversion a safe diff; `Tests/DialogueEditTests.cs` covers where a save writes and what it replaces.

**Done when:** the intro NPCs' conversations are `.yarn` files, behave identically in game, and the dialogue editor still opens and saves them. ✅ — with the caveat that a hands-on playthrough of the six NPCs is the half of the acceptance no test can cover.

## Phase 3 — Game-state bridge (world flags) *(done)*

The condition vocabulary covered quests, inventory, defeated NPCs, and party room. What it couldn't express was the ad-hoc `$met_dockmaster` flag a writer reaches for constantly.

1. **`GameState.Flags`** — a `Dictionary<string, string>` in the world-state bucket, beside `DefeatedNpcs`, saved with everything else (`SaveVersion` 8; pre-v8 saves load with no flags, so a restored old save is simply greeted as a first meeting — no migration step needed). Values are strings, so a flag can carry a little state (`"angry"`, `"2"`) as well as a yes/no. Keys are normalized (trimmed, lower-cased) by the accessors rather than by a case-insensitive comparer, because `System.Text.Json` rebuilds the dictionary with the default comparer on load — a comparer set in the initializer would have applied to new games only.
2. **Vocabulary:** the `<<set_flag met_hale>>` effect (value defaults to `"true"`; an explicitly empty one clears the flag) and the `flag("met_hale")` / `flag("hale_mood", "angry")` condition. "Set" means *holds something that isn't an explicit no*, so `set_flag met_hale false` reads back as not met rather than as a strange value. `flag("met_hale", "")` asks whether a flag is **unset** — the way to write the negative case in a vocabulary that has no `not`.
3. **Stat- and party-gated options:** `stat("Charisma", 12)` (the party leader's stat, since the leader is who the player speaks as) and `party_size(2)`, both "at least" comparisons like `has_item`. Leader *name* was on this list and was deliberately left off: gating on a display string isn't a game fact, and what a writer actually wants there is text interpolation (`{$leader}`), which is a separate feature the graph has no slot for yet.
4. **`flag` / `flags` console verbs** (`EditorFlagCommands`, debug-gated with the rest of the console): set, clear, read, and list flags. Flags are the one piece of save state with no UI anywhere — no quest log, no inventory tab — so without these an author could only reach a flag-gated branch by replaying the conversation that sets it. `flag get` also spells out when a value like `false` exists but reads as unset.
5. **`intro.shopkeeper.yarn` is the demo:** the first visit sets `met_shopkeeper` and every visit after gets the returning-customer greeting. Both greetings share the "Just looking" reply, which is why the shared node keeps its own `title:`.
6. **Tests:** `Tests/WorldFlagTests.cs` (the store, truthiness, key normalization, a save round trip, a hand-written pre-v8 save loading with no flags, every new verb's evaluation and validation, and the verbs authored in Yarn surviving a writer round trip), `Tests/EditorFlagCommandTests.cs` (the console verbs), and two cases in `Tests/IntroDialogueBranchTests.cs` — the shopkeeper's second greeting, and the same greeting after a save and reload.

**Done when:** an NPC greets differently on second interaction, and after save/quit/load still remembers you. ✅ — the reload half is a test rather than a hands-on quit-and-restart.

## Phase 4 — Commands (dialogue acts on the world) *(done)*

Most of this vocabulary already existed and is what Yarn commands compile to — the fetch loop this phase was measured by has worked from data since the dialogue-editor migration. What landed here is the part that was still missing, plus a decision about the part that isn't needed:

1. **`<<play_anim Cheering>>` / `<<play_anim Sword_Idle loop>>`** — a gesture on the speaking NPC's rig, the one effect whose only consequence is what the player sees. A one-shot returns the NPC to idle when the clip runs out; a looping one holds until the conversation closes or another gesture replaces it. It reaches the scene through `IDialogueEffectHost.PlayAnimation` like the other scene verbs, so `DialogueEffects` still holds the "no game logic in dialogue glue" line.
2. **A curated clip vocabulary** (`DialogueAnimations.Ids`): ten clips out of the ~100 in the shared KayKit library — idle, talking, waving, cheering, dancing, reaching, picking up, holding out an item, throwing, blade-out. The library also holds swimming, crawling, pistol stances, and a skeleton set, all of which are nonsense in a conversation and none of which would error if authored, so the validator and the editor's dropdown check against the curated list. `Tests/DialogueAnimationTests.cs` checks every name against the files on disk, which is what stops a typo or a removed asset from becoming a gesture that silently never plays.
3. **The rig plumbing:** `CharacterRig.PlayExtra` loads any clip from `ThirdParty/AnimationLibrary/` on demand into a per-rig library (duplicating it per loop mode, the same care `BattleScene` takes with its cached resources), and `Npc` gives a dialogue gesture priority over the behaviors' locomotion animation. That last part matters more than it sounds: behaviors call `PlayLocomotion` *every physics frame* — `Halt` does, even while the body is frozen for a conversation — so without the guard a gesture would be stomped by idle on the very next frame. Stationary NPCs never call it at all, so an `AnimationFinished` handler, not the behaviors, is what unfreezes them from a one-shot's last pose.
4. **`<<set_npc_flag NpcId Flag Value>>` (NPC plan Phase 4) is subsumed by Phase 3's flag store** — a per-NPC flag is `<<set_flag hale_mood angry>>` with a naming convention, not a second store. Worth revisiting only if NPC state ever needs to be enumerable per NPC (a UI listing what an NPC remembers).
5. **Camera commands were not added**, and that is the standing decision, not an omission: dialogue keeps the gameplay camera for v1 (see the decisions table), so a `<<camera ...>>` verb would be authoring against a system that hasn't been designed yet.
6. **In the content:** Hale reaches out and takes the cube (`PickUp`), Marlow holds the keycard out (`Use_Item`), a recruit celebrates (`Cheering`), Trader Moss waves a returning customer in (`Waving`), and Vex keeps his blade out for the whole confrontation (`Sword_Idle loop`) — one use of each code path, asserted in `Tests/IntroDialogueBranchTests.cs`.

**Done when:** a full "fetch" loop authored purely in Yarn works: NPC starts a quest, notices the fetched item, takes it, and rewards the player. ✅ — `intro.dockmaster_hale.yarn` is that loop, and `Tests/IntroDialogueBranchTests.cs` walks it. Whether the gestures *look* right on the rigs is the part only a playthrough can answer.

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
