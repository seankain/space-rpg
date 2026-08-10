# Quest System — Implementation Plan

Goal: main and side quests with stages, prerequisites, a journal UI, and completion rewards — built on the existing stubs in `Scripts/Data/Quest.cs` (`Quest`, `QuestStage`, `QuestPrereqFlag`, `QUESTSUCCESSSTATE`).

Depends on: dialogue plan Phase 4 (quest commands) for the primary way quests start and advance — **done**, `set_quest`/`advance_quest` are shipped `DialogueEffects` verbs; inventory plan for item objectives/rewards — the inventory itself is done, quest-facing rewards are not.

## Where we are

*(Reviewed against the tree at `460ce9e`.)* The plan's own phases have been worked on out of order: the parts the [quest-markers plan](quest-markers.md) needed were built first, so **quests start, advance, complete, save, and display — but there are still no stages, no `QuestManager`, and no rewards.**

What exists:

- **Definitions** are `Quest` in a static `QuestCatalog` (`Id`, `Title`, `Description`, `SideQuest`, `PrereqQuests`, `Markers`), keyed by id, mirroring `ItemCatalog`. Two quests ship: "Return the Maguffin" (main) and "Clear the Deck" (side).
- **Progress** is `QuestProgress` (`QuestId`, `State`, `CurrentStageNumber`) in `GameState.Quests`, saved since save version 3, moved only through `GameState.SetQuestState` / `GetOrAddQuestProgress`.
- **Transitions** come from dialogue (`<<set_quest 1 Success>>`, `<<advance_quest 1>>` → `DialogueEffects.MoveQuest`) and from the developer console (`quest start|set|stage|advance`, `quests`). `MoveQuest` logs a `GameEventKind.Quest` entry with `notify` set, so a quest move already raises a corner toast through `EventToasts`.
- **Reads** are `GameState.GetQuestState`, exposed to authoring as the `quest_state(1, "Success")` predicate and the `quest(1)` query.
- **Journal**: `QuestLogMenu` drives the Quests tab — Main / Side / Completed / Failed sections, a details panel, an objective list, a Track toggle, and Show on Map.
- **Markers and tracking**: `Quest.Markers`, `QuestMarkerResolver`, `QuestTargetLocator`, `QuestMarkerPlacements`, `GameState.TrackedQuestId` (save version 10) and the static `QuestTracking.Changed` hook — all four phases of [quest-markers.md](quest-markers.md), which is where the "what do I do next" text in the journal comes from today.

What is still missing, and is the substance of the rest of this plan:

- **Stages.** `QuestStage` and `QuestStagePrereq` are the same empty stubs they were on day one, and no `Quest` carries a stage list. `QuestProgress.CurrentStageNumber` is written by the console's `quest stage` / `quest advance` verbs and read by nothing — deliberately unvalidated, since there is no stage list to validate against.
- **Prerequisites.** `PrereqQuests` is authored (as an empty list, twice) and never read. Nothing enforces a prereq or validates that a prereq names a real quest.
- **A `QuestManager`.** Its responsibilities are spread across `GameState` (state + tracking), `DialogueEffects` (transitions + logging), and `EditorQuestCommands` (console). There are no `QuestStarted`/`QuestStageChanged`/`QuestCompleted` events; the generic `GameEventLog.Recorded` hook carries quest moves instead, and console-driven moves don't log at all.
- **Rewards.** No reward data on a quest definition. The keycard for "Clear the Deck" is handed over by a `give_item` in Marlow's dialogue, not by the quest completing.
- **Two known leftovers:** `Quest.SuccessState` (`Quest.cs:11`) survived the definition/progress split and is now dead — nothing reads it — and `advance_quest` is a *state* verb (Unstarted → InProgress → Success) while the console's `quest advance` is a *stage* verb, so the same word means two things.

---

## Phase 1 — Quest data model and catalog *(partly done: the split shipped, stages did not)*

1. ~~Split **definition** from **progress** (same pattern as items)~~ — **done**, except for two details:
   - Definitions live in the static `QuestCatalog`, **not** in `res://Data/quests.json`. Deliberate for now: it mirrors `ItemCatalog`, and the [markers plan](quest-markers.md) records the same decision ("when the catalog moves to JSON they move with it"). JSON authoring is still worth doing, and is the one piece of this item outstanding.
   - `SuccessState` was **not** moved off `Quest`. The field is dead rather than wrong — progress genuinely lives in `QuestProgress` — but it should be deleted before someone writes to it.
2. Flesh out stages: give `QuestStage` an **objective** description and an optional completion condition type (manual/scripted, item-in-inventory, npc-flag) — start with "scripted only" (advanced explicitly by dialogue/trigger commands) and add auto-checked conditions later; delete or fill `QuestStagePrereq` accordingly. **Not started.** Note markers arrived first and cover part of what stages were meant to give the player: `QuestMarker.VisibleWhen` already expresses "where to go now" as a condition on state, so stages should reuse that vocabulary (a stage-number condition verb) rather than replace it — see quest-markers Phase 5 item 5.
3. `QuestCatalog` loader with validation (stage numbers contiguous, prereq ids exist). **Partly done:** `QuestCatalog.ValidateMarkers` runs after registration and throws on a bad marker. Stage and prereq validation don't exist yet — there is nothing to validate for stages, but the prereq-ids check is buildable today.
4. ~~Bump `SaveVersion` with a migration for the progress list~~ — **done** at save version 3 (`SaveRepository` migrates an older save to an empty quest log); tracking later took version 10.

**Done when:** unit tests cover catalog validation and progress round-trips through save/load. *(Progress round-trips are covered by `Tests/QuestTrackingTests.cs`, and catalog validation by `Tests/QuestMarkerTests.cs` — but only the marker half of it. Stage/prereq coverage waits on stages and prereq checks existing.)*

## Phase 2 — QuestManager *(not built — the jobs landed elsewhere)*

The APIs below all exist as behaviour; none of them exist as a `QuestManager`. Before building one, decide whether it earns its place — the argument for it is items 1's prereq check and the events in item 1e, not the state getters/setters, which are fine where they are.

1. `QuestManager` autoload (or a plain class owned alongside `SaveManager`) exposing:
   - `StartQuest(id)` — validates prereqs via `QuestPrereqFlag` against current progress states. **Today:** `GameState.SetQuestState(id, InProgress)` via `<<set_quest>>` or `quest start`, with **no prereq validation anywhere**. This is the clearest gap in the phase.
   - `AdvanceQuest(id, stage)` / `CompleteQuest(id)` / `FailQuest(id)`. **Today:** `set_quest`/`advance_quest` move the *success state*; nothing advances a stage except the console writing a number nothing reads.
   - `GetState(id)` — powering the dialogue plan's `quest_state("id")` Yarn function. **Done** as `GameState.GetQuestState`; both `quest_state` and `quest()` are wired to it.
   - Events: `QuestStarted`, `QuestStageChanged`, `QuestCompleted` (UI + reward hooks subscribe). **Not built.** The nearest thing is `GameEventLog.Recorded` (a `Quest`-kind entry per state move, which is what drives the toast) and `QuestTracking.Changed` (tracking only). Note `DialogueEffects.MoveQuest` is where quest logging lives, so console moves are silent — a reason to pull both into one place.
2. Wire the dialogue commands (`<<start_quest>>`, `<<advance_quest>>`) to these APIs. **Done in substance**, against `GameState` rather than a manager; the shipped verbs are `set_quest` and `advance_quest` (there is no `start_quest` — `set_quest <id> InProgress` is how a quest starts).
3. **World triggers:** a `QuestTrigger` Area3D node (enter area → advance stage X of quest Y, once) for non-dialogue beats like "reach the cargo bay". **Not started** — `Scripts/World/` has `Door` and `Portal` but no quest trigger. Blocked on stages being real.

**Done when:** the fetch-quest loop from the dialogue plan drives quest state through `QuestManager` and survives save/load at every stage. *(The fetch loop does run end-to-end and survives save/load — through `GameState`, and with no stages in it.)*

## Phase 3 — Journal UI and player feedback *(mostly done)*

1. ~~**Journal tab** in `InGameMenu`~~ — **done** (`QuestLogMenu`): active quests in Main / Side sections per the `SideQuest` flag, plus Completed and Failed, each row showing title, and a details panel with status and description. The current-stage subtitle is the one missing piece and waits on Phase 1's stages; the objective lines the panel shows today come from the markers plan's resolver, not from stages.
2. **HUD notifications:** **done by a different route than planned.** Quest moves are recorded as `GameEventKind.Quest` entries with `notify` set, and the generic `EventToasts` autoload turns any notifying entry into a corner toast — so there is no quest-specific subscription to write. Still outstanding: the "tracked quest objective line" on `PlayerHud`, which is an empty shell; the Map tab carries that line instead.
3. Mark quest-relevant NPCs (indicator over quest givers with an available quest — reads prereq-satisfied, unstarted quests targeting that NPC). **Not started**, and it needs two things that don't exist: prereq evaluation (Phase 2) and a link from a quest to the NPC that offers it. `NpcRole.RequiredQuestId` gates whether a role is *available*, which is the opposite direction and not a substitute.

**Done when:** a player can follow the fetch quest end-to-end using only the journal and HUD cues. *(Reachable today — journal objectives, map markers, and toasts cover it — with no on-NPC indicator to point the player at the quest in the first place.)*

## Phase 4 — Rewards and depth *(not started)*

1. **Rewards** in quest definitions: XP (build plan's `GrantXp`, applied to the whole party), items (inventory plan), currency. Granted on `QuestCompleted`. Nothing on `Quest` describes a reward; today they are authored as `give_item`/`credits` effects in the turn-in dialogue. Note `GrantXp` does not exist yet either — `CharacterEntity.ExperiencePoints` is a field, and only enemies carry an `XpReward` — so the XP half depends on the build plan landing first.
2. **Auto-checked objectives:** item-count and npc-flag stage conditions evaluated on relevant events (inventory changed, flag set) rather than per-frame polling. Marker `VisibleWhen` conditions are the same idea one layer up, but they are re-evaluated per map refresh rather than on an event; a stage condition should be able to share whatever evaluation this builds.
3. **Branching outcomes:** quests completable in multiple ways — model as stages with alternate next-stage transitions; `Failed` state paths (e.g. quest NPC leaves) authored via dialogue/trigger commands. Partial precedent exists without stages: Hale's turn-in can be refused into a battle, and beating him leaves the quest line intact.
4. Multi-quest chains via existing `QuestPrereqFlag` (e.g. side quest unlocked only after main quest 3 `Success`) — blocked on prereqs being read at all (Phase 2).

**Done when:** completing a quest grants XP/items, and one authored quest has two distinct completion paths.

## Suggested next slice

Phase 1 item 2 (stages) is the keystone: Phase 2's stage APIs and `QuestTrigger`, Phase 3's stage subtitle, and Phase 4's branching all wait on it. Two smaller items are worth doing alongside or first, since neither depends on stages:

- Read `PrereqQuests` — enforce it in whatever starts a quest, and validate prereq ids in `QuestCatalog` beside `ValidateMarkers`. That unblocks Phase 3's quest-giver indicator and Phase 4's chains.
- Delete the dead `Quest.SuccessState`, and settle the `advance_quest` (state) vs. `quest advance` (stage) name collision before stages make it ambiguous in play rather than only in the source.

## Decisions to settle early

| Decision | Recommendation |
|----------|----------------|
| Where quest logic lives | Definitions are data; transitions happen only through `QuestManager` APIs, driven by dialogue commands and triggers — no quest-specific C# per quest. *(Holding in spirit: no per-quest C# exists. In practice transitions go through `GameState.SetQuestState`, and the "one place" property is real enough that the markers plan relies on it — tracking follows the quest log from inside `SetQuestState` precisely so every caller behaves the same.)* |
| Stage progression shape | Linear stage list with explicit scripted advancement first; add conditions/branching in Phase 4 only once real content demands it. *(Unchanged — and note the marker condition vocabulary now exists, so "scripted advancement" and "condition-driven visibility" can coexist rather than compete.)* |
| Journal scope | Text-only journal for v1 — no map markers until a map system exists. *(Superseded: the map exists and markers shipped — [quest-markers.md](quest-markers.md), all four phases — which is also where the journal's objective lines and the tracked quest came from.)* |
| Catalog format | Static C# registry for now, mirroring `ItemCatalog`; JSON authoring (Phase 1 item 1) is still wanted, and markers move with the definitions when it happens. |
