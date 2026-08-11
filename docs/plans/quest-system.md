# Quest System — Implementation Plan

Goal: main and side quests with stages, prerequisites, a journal UI, and completion rewards — built on the existing stubs in `Scripts/Data/Quest.cs` (`Quest`, `QuestStage`, `QuestPrereqFlag`, `QUESTSUCCESSSTATE`).

Depends on: dialogue plan Phase 4 (quest commands) for the primary way quests start and advance — **done**, `set_quest`/`advance_quest` are shipped `DialogueEffects` verbs; inventory plan for item objectives/rewards — the inventory itself is done, quest-facing rewards are not.

## Where we are

*(Reviewed against the tree at `872c474`, then updated as Phases 1 and 2 landed.)* The plan's own phases have been worked on out of order: the parts the [quest-markers plan](quest-markers.md) needed were built first. **Quests start, advance, complete, save, display, run on authored stages, and move through one place that reads prerequisites — what is left is rewards and depth.**

What exists:

- **Definitions** are `Quest` in a static `QuestCatalog` (`Id`, `Title`, `Description`, `SideQuest`, `PrereqQuests`, `Stages`, `Markers`), keyed by id, mirroring `ItemCatalog`. Two quests ship: "Return the Maguffin" (main) and "Clear the Deck" (side), two stages each.
- **Progress** is `QuestProgress` (`QuestId`, `State`, `CurrentStageNumber`) in `GameState.Quests`, saved since save version 3, moved only through `GameState.SetQuestState` / `SetQuestStage` / `AdvanceQuestStage`.
- **Transitions** all run through `QuestManager` (`StartQuest`, `SetState`, `CompleteQuest`, `FailQuest`, `AdvanceState`, `SetStage`, `AdvanceStage`, `ReachStage`), whoever asked for them: dialogue (`<<set_quest 1 Success>>`, `<<advance_quest 1>>`, `<<set_stage 1 next>>`), the developer console (`quest start|set|stage|advance`, `quests`), and world beats (`QuestTrigger`, a quest-carrying `Pickup`). The manager checks prerequisites, writes the `GameEventKind.Quest` log line with `notify` set — so any move raises a corner toast through `EventToasts` — and announces itself on the static `QuestManager.Moved` hook.
- **Reads** are `GameState.GetQuestState` / `GetQuestStage` / `GetCurrentStage`, exposed to authoring as the `quest_state(1, "Success")` predicate and the `quest(1)` / `quest_stage(1)` queries.
- **Journal**: `QuestLogMenu` drives the Quests tab — Main / Side / Completed / Failed sections, a details panel with the current stage's subtitle, an objective list, a Track toggle, and Show on Map.
- **Markers and tracking**: `Quest.Markers`, `QuestMarkerResolver`, `QuestTargetLocator`, `QuestMarkerPlacements`, `GameState.TrackedQuestId` (save version 10) and the static `QuestTracking.Changed` hook — all four phases of [quest-markers.md](quest-markers.md), which is where the journal's objective lines come from.
- **Catalog validation** is `QuestCatalog.Problems`, run over every definition in the static constructor and reported as one message: bad markers, stage numbers that don't run 1..n in order, a stage with no subtitle, and prerequisites that name a missing quest, the quest itself, or the same quest twice.

- **World beats** are `QuestTrigger` (an Area3D: the player walks in, the quest reaches the stage that place stands for) and the same two fields on `Pickup` (collecting the Maguffin Cube puts quest 1 on stage 2, which is the fetch quest's middle beat). Both ask for "at least this stage" on a quest in progress, so they are idempotent and no trigger stores anything in the save.

What is still missing, and is the substance of the rest of this plan:

- **Rewards.** No reward data on a quest definition. The keycard for "Clear the Deck" is handed over by a `give_item` in Marlow's dialogue, not by the quest completing. `QuestManager.Moved` is the hook a reward grant subscribes to.
- **Auto-checked objectives.** "Clear the Deck" still has no way to reach its second stage in play: the beat is winning a fight, which is neither a conversation nor a place. That wants Phase 4's event-driven stage conditions (`npc_defeated`), not a third bespoke hook.
- **Prerequisites in the fiction.** They are validated and enforced, but no shipped quest declares one, so the machinery is unexercised by content — Phase 4's quest chains are what will use it.
- **A quest-giver indicator** (Phase 3 item 3), which now has its prerequisite check but still needs a link from a quest to the NPC who offers it.

---

## Phase 1 — Quest data model and catalog *(done, bar JSON authoring)*

1. ~~Split **definition** from **progress** (same pattern as items)~~ — **done**. `Quest.SuccessState`, the dead leftover of the split, is **deleted**. One piece outstanding: definitions live in the static `QuestCatalog`, **not** in `res://Data/quests.json`. Deliberate for now — it mirrors `ItemCatalog`, and the [markers plan](quest-markers.md) records the same decision ("when the catalog moves to JSON they move with it") — but JSON authoring is still worth doing, and markers and stages move with the definitions when it happens.
2. ~~Flesh out stages~~ — **done**, scripted-only as planned. `QuestStage` is `StageNumber` + `SubtitleText` (the journal's "what am I doing" line) + `Description`; `Quest.Stages` holds them in order, and a quest may declare none, which is what every quest was until now. The empty `QuestStagePrereq` stub is **deleted** rather than filled: auto-checked completion conditions are Phase 4, and when they arrive they should be a `ConditionRef` like `QuestMarker.VisibleWhen` rather than a second bespoke type.
   - Stages **reuse the condition vocabulary** rather than replacing it, as planned: `quest_stage(1) >= 2` is a `DialogueQueries` query, so a marker, a dialogue branch, or a choice can gate on a stage exactly the way it gates on anything else (quest-markers Phase 5 item 5).
   - Moving a stage is `<<set_stage <questId> <n|next>>>` in dialogue and `quest stage <questId> <n|next>` on the console, both bounded by the definition. `GameState` owns the writes (`SetQuestStage`, `AdvanceQuestStage`), refuses a stage a quest doesn't declare, puts a quest on stage 1 when it is taken, and drops the stage when a quest is wound back to Unstarted — so stage 0 only ever means "not on one", including for a save written before stages existed.
3. ~~`QuestCatalog` loader with validation (stage numbers contiguous, prereq ids exist)~~ — **done** as `QuestCatalog.Problems(definitions)`, which the static constructor runs over the whole catalog and throws on. It covers markers (as before), stage numbering and subtitles, and prerequisites naming a real quest, not the quest itself, and not twice. Taking the definitions as an argument is what lets the tests check content the catalog would refuse.
4. ~~Bump `SaveVersion` with a migration for the progress list~~ — **done** at save version 3 (`SaveRepository` migrates an older save to an empty quest log); tracking later took version 10. Stages needed **no new version**: `CurrentStageNumber` has been in the format since quests were, and an old save's 0 is already the "no stage" value.

**Done when:** unit tests cover catalog validation and progress round-trips through save/load. ***Met*** — `Tests/QuestStageTests.cs` covers stage/prereq validation against fabricated definitions, the stage rules on `GameState`, the `quest_stage` query, the `set_stage` effect, and the save round-trip (including a pre-stages save rejoining the ladder); `Tests/QuestTrackingTests.cs` and `Tests/QuestMarkerTests.cs` cover tracking round-trips and marker validation.

The one thing stages didn't have when Phase 1 landed was anything to advance them *in play*; Phase 2's world beats are that.

## Phase 2 — QuestManager *(done)*

Built, having decided it earns its place on the two grounds this section named: the prerequisite check in item 1, and the events in item 1e. Storage stays where it was — a save is still the progress list on `GameState`, and tracking still follows the quest log from inside `SetQuestState` — but every *transition* now goes through `QuestManager`, which is what makes a prerequisite readable and a move announceable at all.

It is a **static engine-free class taking the `GameState`**, not an autoload holding one (the plan allowed either). Every caller already has a state in hand — a dialogue context, a console command, a world node reading `SaveManager` — and this way the whole thing is unit-tested without booting Godot, like `QuestMarkerResolver` and the console command classes beside it.

1. ~~`QuestManager` exposing…~~ — **done**:
   - `StartQuest(state, id)` — checks `PrereqQuests` against current progress and returns null when the quest is now in progress, else why not (the "null when fine, else the reason" shape the validators use). Starting a quest already in progress is not a complaint, so a conversation replayed from the top is silent. `PrereqProblem` is public for the quest-giver indicator (Phase 3 item 3) to ask "could the party take this?" without starting it.
   - `SetState` / `CompleteQuest` / `FailQuest` / `AdvanceState` for the success state; `SetStage` / `AdvanceStage` / `ReachStage` for stages. `SetState` is the raw move — prerequisites are `StartQuest`'s business, which is what leaves the console's `quest set` as a developer's way past them.
   - `GetState(id)` — unchanged as `GameState.GetQuestState`; reads stay on the state, as this section argued they should.
   - Events: one static `QuestManager.Moved` carrying a `QuestMove` (quest id, kind, from/to state, from/to stage, and the `GameState` it happened in) rather than the three separate `QuestStarted`/`QuestStageChanged`/`QuestCompleted` events sketched here — subscribers filter on `Kind`, and a new kind then costs nobody a second subscription. Static for the reason `GameEventLog.Recorded` is: listeners outlive any one `GameState`. `QuestLogMenu` is the first subscriber (the Quests tab now updates while it is open); a Phase 4 reward grant is the next.
   - Logging moved here from `DialogueEffects.MoveQuest`, which is what this section wanted: a console move now writes the same line a conversation's move does, instead of changing the game silently.
2. ~~Wire the dialogue commands to these APIs~~ — **done**: `set_quest`, `advance_quest` and `set_stage` are thin wrappers over the manager, and a `set_quest … InProgress` refused by a prerequisite warns the way any other bad content does.
3. ~~**World triggers**~~ — **done** as `QuestTrigger` (`Scripts/World/`): an Area3D that asks for "at least stage n" on a quest in progress when the player walks in. Idempotent by construction — walking back through, or reloading a save made afterwards, moves nothing — so no trigger stores a fired flag in the save. The stage is named rather than "the next one", because a trigger is a fixed place tied to a specific beat. `Pickup` carries the same two fields, which is how collecting the Maguffin Cube now puts quest 1 on stage 2: the fetch quest's middle beat is an item entering the party's pockets, not a place.
   - **Known gap in this shape:** a beat only fires while you are standing in it. A player who picks the cube up *before* taking the quest never trips that stage, so the journal reads "Find the Maguffin Cube" while the marker — a condition, re-read every time — correctly points at Hale. Phase 4 item 2 is the fix: a stage whose `ReachedWhen` is `has_item:1` is right whenever it is evaluated, in any order.

**Done when:** the fetch-quest loop from the dialogue plan drives quest state through `QuestManager` and survives save/load at every stage. ***Met*** — Hale's conversation starts it (stage 1), the cube's pickup advances it (stage 2), his turn-in completes it, every step through the manager, and `Tests/QuestStageTests.cs` covers the stage half of the round trip. "Clear the Deck" still can't reach its second stage in play; its beat is a battle win, which is Phase 4's auto-checked objectives rather than a fourth kind of hook.

## Phase 3 — Journal UI and player feedback *(mostly done)*

1. ~~**Journal tab** in `InGameMenu`~~ — **done** (`QuestLogMenu`): active quests in Main / Side sections per the `SideQuest` flag, plus Completed and Failed, each row showing title, and a details panel with status, description, and — for a quest still in progress — a "Current: …" line carrying the stage subtitle. The objective lines below it come from the markers plan's resolver, not from stages; the two say the same thing for the shipped content and will diverge once a quest has more beats than markers.
2. **HUD notifications:** **done by a different route than planned.** Quest moves are recorded as `GameEventKind.Quest` entries with `notify` set, and the generic `EventToasts` autoload turns any notifying entry into a corner toast — so there is no quest-specific subscription to write. Still outstanding: the "tracked quest objective line" on `PlayerHud`, which is an empty shell; the Map tab carries that line instead.
3. Mark quest-relevant NPCs (indicator over quest givers with an available quest — reads prereq-satisfied, unstarted quests targeting that NPC). **Not started**, but half-unblocked: `QuestManager.PrereqProblem` is the "could the party take this?" check it needed. What is still missing is a link from a quest to the NPC that offers it. `NpcRole.RequiredQuestId` gates whether a role is *available*, which is the opposite direction and not a substitute.

**Done when:** a player can follow the fetch quest end-to-end using only the journal and HUD cues. *(Reachable today — journal objectives, map markers, and toasts cover it — with no on-NPC indicator to point the player at the quest in the first place.)*

## Phase 4 — Rewards and depth *(not started)*

1. **Rewards** in quest definitions: XP (build plan's `GrantXp`, applied to the whole party), items (inventory plan), currency. Granted on `QuestCompleted`. Nothing on `Quest` describes a reward; today they are authored as `give_item`/`credits` effects in the turn-in dialogue. Note `GrantXp` does not exist yet either — `CharacterEntity.ExperiencePoints` is a field, and only enemies carry an `XpReward` — so the XP half depends on the build plan landing first.
2. **Auto-checked objectives:** item-count and npc-flag stage conditions evaluated on relevant events (inventory changed, flag set) rather than per-frame polling. Marker `VisibleWhen` conditions are the same idea one layer up, but they are re-evaluated per map refresh rather than on an event; a stage condition should be able to share whatever evaluation this builds. This is what "Clear the Deck" needs to reach stage 2 — its beat is beating Vex, which `QuestTrigger` (a place) and `Pickup` (a thing) can't express.
3. **Branching outcomes:** quests completable in multiple ways — model as stages with alternate next-stage transitions; `Failed` state paths (e.g. quest NPC leaves) authored via dialogue/trigger commands. Partial precedent exists without stages: Hale's turn-in can be refused into a battle, and beating him leaves the quest line intact.
4. Multi-quest chains via existing `QuestPrereqFlag` (e.g. side quest unlocked only after main quest 3 `Success`) — **unblocked**: prerequisites are validated at registration and enforced by `QuestManager.StartQuest`, so a chain is now purely a content question. Nothing shipped declares one yet.

**Done when:** completing a quest grants XP/items, and one authored quest has two distinct completion paths.

## Suggested next slice

Phases 1 and 2 are in, so what remains is Phase 4's depth and the two Phase 3 leftovers. In order:

- **Rewards on the definition** (Phase 4 item 1) — the last thing authored in dialogue that ought to belong to the quest, and `QuestManager.Moved` is the hook it subscribes to. The XP half waits on the build plan's `GrantXp`; items and credits don't.
- **Auto-checked objectives** (Phase 4 item 2) — the third and last way a stage can move, and the one "Clear the Deck" needs. A stage gains a `ReachedWhen` `ConditionRef` re-used from the marker vocabulary, evaluated when the state it reads changes.
- **The quest-giver indicator** (Phase 3 item 3) — now only needs a quest→NPC link; the prerequisite check it was waiting for exists.

## Decisions to settle early

| Decision | Recommendation |
|----------|----------------|
| Where quest logic lives | Definitions are data; transitions happen only through `QuestManager` APIs, driven by dialogue commands and triggers — no quest-specific C# per quest. *(Held, and now literally: `QuestManager` exists and every dialogue verb, console verb and world beat goes through it. Storage and tracking stay on `GameState`, which is what the markers plan relies on.)* |
| Stage progression shape | Linear stage list with explicit scripted advancement first; add conditions/branching in Phase 4 only once real content demands it. *(Held: `Quest.Stages` is a linear list numbered 1..n, moved only by `set_stage` / `quest stage`. The two vocabularies coexist as hoped — a stage is scripted, and `quest_stage(1) >= 2` gates a marker or a branch the same way any other condition does.)* |
| Journal scope | Text-only journal for v1 — no map markers until a map system exists. *(Superseded: the map exists and markers shipped — [quest-markers.md](quest-markers.md), all four phases — which is also where the journal's objective lines and the tracked quest came from.)* |
| Catalog format | Static C# registry for now, mirroring `ItemCatalog`; JSON authoring (Phase 1 item 1) is still wanted, and markers move with the definitions when it happens. |
