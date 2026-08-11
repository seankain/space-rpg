# Quest System — Implementation Plan

Goal: main and side quests with stages, prerequisites, a journal UI, and completion rewards — built on the existing stubs in `Scripts/Data/Quest.cs` (`Quest`, `QuestStage`, `QuestPrereqFlag`, `QUESTSUCCESSSTATE`).

Depends on: dialogue plan Phase 4 (quest commands) for the primary way quests start and advance — **done**, `set_quest`/`advance_quest` are shipped `DialogueEffects` verbs; inventory plan for item objectives/rewards — the inventory itself is done, quest-facing rewards are not.

## Where we are

*(Reviewed against the tree at `872c474`, then updated when Phase 1 landed.)* The plan's own phases have been worked on out of order: the parts the [quest-markers plan](quest-markers.md) needed were built first. **Quests start, advance, complete, save, display, and now run on authored stages — there is still no `QuestManager` and no rewards.**

What exists:

- **Definitions** are `Quest` in a static `QuestCatalog` (`Id`, `Title`, `Description`, `SideQuest`, `PrereqQuests`, `Stages`, `Markers`), keyed by id, mirroring `ItemCatalog`. Two quests ship: "Return the Maguffin" (main) and "Clear the Deck" (side), two stages each.
- **Progress** is `QuestProgress` (`QuestId`, `State`, `CurrentStageNumber`) in `GameState.Quests`, saved since save version 3, moved only through `GameState.SetQuestState` / `SetQuestStage` / `AdvanceQuestStage`.
- **Transitions** come from dialogue (`<<set_quest 1 Success>>`, `<<advance_quest 1>>` → `DialogueEffects.MoveQuest`; `<<set_stage 1 next>>` for stages) and from the developer console (`quest start|set|stage|advance`, `quests`). `MoveQuest` logs a `GameEventKind.Quest` entry with `notify` set, so a quest move already raises a corner toast through `EventToasts`; a stage move logs "New objective: …" the same way.
- **Reads** are `GameState.GetQuestState` / `GetQuestStage` / `GetCurrentStage`, exposed to authoring as the `quest_state(1, "Success")` predicate and the `quest(1)` / `quest_stage(1)` queries.
- **Journal**: `QuestLogMenu` drives the Quests tab — Main / Side / Completed / Failed sections, a details panel with the current stage's subtitle, an objective list, a Track toggle, and Show on Map.
- **Markers and tracking**: `Quest.Markers`, `QuestMarkerResolver`, `QuestTargetLocator`, `QuestMarkerPlacements`, `GameState.TrackedQuestId` (save version 10) and the static `QuestTracking.Changed` hook — all four phases of [quest-markers.md](quest-markers.md), which is where the journal's objective lines come from.
- **Catalog validation** is `QuestCatalog.Problems`, run over every definition in the static constructor and reported as one message: bad markers, stage numbers that don't run 1..n in order, a stage with no subtitle, and prerequisites that name a missing quest, the quest itself, or the same quest twice.

What is still missing, and is the substance of the rest of this plan:

- **Prerequisite enforcement.** `PrereqQuests` is now *validated* but still never *read*: nothing checks a prereq before a quest starts, because nothing owns "starting a quest" (Phase 2). Both shipped quests declare an empty list.
- **A `QuestManager`.** Its responsibilities are spread across `GameState` (state, stages, tracking), `DialogueEffects` (transitions + logging), and `EditorQuestCommands` (console). There are no `QuestStarted`/`QuestStageChanged`/`QuestCompleted` events; the generic `GameEventLog.Recorded` hook carries quest moves instead, and console-driven moves don't log at all.
- **Something to advance a stage in play.** Stages advance only when a script says so, and the two shipped quests turn on beats that are not conversations — picking the cube up, winning a fight — so in play they sit on stage 1 until the console moves them. The missing piece is Phase 2's `QuestTrigger` (and Phase 4's auto-checked conditions); the marker `VisibleWhen` conditions carry those two beats in the meantime, which is why the journal and map still say the right thing.
- **Rewards.** No reward data on a quest definition. The keycard for "Clear the Deck" is handed over by a `give_item` in Marlow's dialogue, not by the quest completing.

---

## Phase 1 — Quest data model and catalog *(done, bar JSON authoring)*

1. ~~Split **definition** from **progress** (same pattern as items)~~ — **done**. `Quest.SuccessState`, the dead leftover of the split, is **deleted**. One piece outstanding: definitions live in the static `QuestCatalog`, **not** in `res://Data/quests.json`. Deliberate for now — it mirrors `ItemCatalog`, and the [markers plan](quest-markers.md) records the same decision ("when the catalog moves to JSON they move with it") — but JSON authoring is still worth doing, and markers and stages move with the definitions when it happens.
2. ~~Flesh out stages~~ — **done**, scripted-only as planned. `QuestStage` is `StageNumber` + `SubtitleText` (the journal's "what am I doing" line) + `Description`; `Quest.Stages` holds them in order, and a quest may declare none, which is what every quest was until now. The empty `QuestStagePrereq` stub is **deleted** rather than filled: auto-checked completion conditions are Phase 4, and when they arrive they should be a `ConditionRef` like `QuestMarker.VisibleWhen` rather than a second bespoke type.
   - Stages **reuse the condition vocabulary** rather than replacing it, as planned: `quest_stage(1) >= 2` is a `DialogueQueries` query, so a marker, a dialogue branch, or a choice can gate on a stage exactly the way it gates on anything else (quest-markers Phase 5 item 5).
   - Moving a stage is `<<set_stage <questId> <n|next>>>` in dialogue and `quest stage <questId> <n|next>` on the console, both bounded by the definition. `GameState` owns the writes (`SetQuestStage`, `AdvanceQuestStage`), refuses a stage a quest doesn't declare, puts a quest on stage 1 when it is taken, and drops the stage when a quest is wound back to Unstarted — so stage 0 only ever means "not on one", including for a save written before stages existed.
3. ~~`QuestCatalog` loader with validation (stage numbers contiguous, prereq ids exist)~~ — **done** as `QuestCatalog.Problems(definitions)`, which the static constructor runs over the whole catalog and throws on. It covers markers (as before), stage numbering and subtitles, and prerequisites naming a real quest, not the quest itself, and not twice. Taking the definitions as an argument is what lets the tests check content the catalog would refuse.
4. ~~Bump `SaveVersion` with a migration for the progress list~~ — **done** at save version 3 (`SaveRepository` migrates an older save to an empty quest log); tracking later took version 10. Stages needed **no new version**: `CurrentStageNumber` has been in the format since quests were, and an old save's 0 is already the "no stage" value.

**Done when:** unit tests cover catalog validation and progress round-trips through save/load. ***Met*** — `Tests/QuestStageTests.cs` covers stage/prereq validation against fabricated definitions, the stage rules on `GameState`, the `quest_stage` query, the `set_stage` effect, and the save round-trip (including a pre-stages save rejoining the ladder); `Tests/QuestTrackingTests.cs` and `Tests/QuestMarkerTests.cs` cover tracking round-trips and marker validation.

The one thing stages don't have yet is anything that advances them *in play*: see "Something to advance a stage in play" above, which is Phase 2's `QuestTrigger`.

## Phase 2 — QuestManager *(not built — the jobs landed elsewhere)*

The APIs below all exist as behaviour; none of them exist as a `QuestManager`. Before building one, decide whether it earns its place — the argument for it is items 1's prereq check and the events in item 1e, not the state getters/setters, which are fine where they are.

1. `QuestManager` autoload (or a plain class owned alongside `SaveManager`) exposing:
   - `StartQuest(id)` — validates prereqs via `QuestPrereqFlag` against current progress states. **Today:** `GameState.SetQuestState(id, InProgress)` via `<<set_quest>>` or `quest start`, with **no prereq validation anywhere**. This is the clearest gap in the phase.
   - `AdvanceQuest(id, stage)` / `CompleteQuest(id)` / `FailQuest(id)`. **Today:** `set_quest`/`advance_quest` move the *success state*, `set_stage` and `quest stage` move the stage, both against `GameState` rather than a manager. The name collision this list once carried is settled: "advance" means the state in both vocabularies, "stage" means the stage in both.
   - `GetState(id)` — powering the dialogue plan's `quest_state("id")` Yarn function. **Done** as `GameState.GetQuestState`; both `quest_state` and `quest()` are wired to it.
   - Events: `QuestStarted`, `QuestStageChanged`, `QuestCompleted` (UI + reward hooks subscribe). **Not built.** The nearest thing is `GameEventLog.Recorded` (a `Quest`-kind entry per state move, which is what drives the toast) and `QuestTracking.Changed` (tracking only). Note `DialogueEffects.MoveQuest` is where quest logging lives, so console moves are silent — a reason to pull both into one place.
2. Wire the dialogue commands (`<<start_quest>>`, `<<advance_quest>>`) to these APIs. **Done in substance**, against `GameState` rather than a manager; the shipped verbs are `set_quest` and `advance_quest` (there is no `start_quest` — `set_quest <id> InProgress` is how a quest starts).
3. **World triggers:** a `QuestTrigger` Area3D node (enter area → advance stage X of quest Y, once) for non-dialogue beats like "reach the cargo bay". **Not started** — `Scripts/World/` has `Door` and `Portal` but no quest trigger. No longer blocked: stages and the `set_stage`/`AdvanceQuestStage` verbs exist, and this is what the two shipped quests need to move off stage 1 in play (their middle beats are a pickup and a battle win, not conversations).

**Done when:** the fetch-quest loop from the dialogue plan drives quest state through `QuestManager` and survives save/load at every stage. *(The fetch loop runs end-to-end and survives save/load, stage included — through `GameState`, and with nothing moving its stage in play yet.)*

## Phase 3 — Journal UI and player feedback *(mostly done)*

1. ~~**Journal tab** in `InGameMenu`~~ — **done** (`QuestLogMenu`): active quests in Main / Side sections per the `SideQuest` flag, plus Completed and Failed, each row showing title, and a details panel with status, description, and — for a quest still in progress — a "Current: …" line carrying the stage subtitle. The objective lines below it come from the markers plan's resolver, not from stages; the two say the same thing for the shipped content and will diverge once a quest has more beats than markers.
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

Stages, catalog validation, and the two leftovers (`Quest.SuccessState`, the `advance` name collision) all landed with Phase 1. What that opens up, in order:

- **`QuestTrigger`** (Phase 2 item 3) — the smallest thing that makes stages visible in play, and the only reason the shipped quests still sit on stage 1. An Area3D that advances a stage once, plus the same idea on a pickup, moves "Return the Maguffin" through both of its beats without a `QuestManager` existing.
- **Read `PrereqQuests`** (Phase 2 item 1) — validation is in; enforcement needs one place that owns "start a quest". That unblocks Phase 3's quest-giver indicator and Phase 4's chains.
- **Rewards on the definition** (Phase 4 item 1) — the last thing authored in dialogue that ought to belong to the quest.

## Decisions to settle early

| Decision | Recommendation |
|----------|----------------|
| Where quest logic lives | Definitions are data; transitions happen only through `QuestManager` APIs, driven by dialogue commands and triggers — no quest-specific C# per quest. *(Holding in spirit: no per-quest C# exists. In practice transitions go through `GameState.SetQuestState`, and the "one place" property is real enough that the markers plan relies on it — tracking follows the quest log from inside `SetQuestState` precisely so every caller behaves the same.)* |
| Stage progression shape | Linear stage list with explicit scripted advancement first; add conditions/branching in Phase 4 only once real content demands it. *(Held: `Quest.Stages` is a linear list numbered 1..n, moved only by `set_stage` / `quest stage`. The two vocabularies coexist as hoped — a stage is scripted, and `quest_stage(1) >= 2` gates a marker or a branch the same way any other condition does.)* |
| Journal scope | Text-only journal for v1 — no map markers until a map system exists. *(Superseded: the map exists and markers shipped — [quest-markers.md](quest-markers.md), all four phases — which is also where the journal's objective lines and the tracked quest came from.)* |
| Catalog format | Static C# registry for now, mirroring `ItemCatalog`; JSON authoring (Phase 1 item 1) is still wanted, and markers move with the definitions when it happens. |
