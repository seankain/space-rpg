# Quest System — Implementation Plan

Goal: main and side quests with stages, prerequisites, a journal UI, and completion rewards — built on the existing stubs in `Scripts/Data/Quest.cs` (`Quest`, `QuestStage`, `QuestPrereqFlag`, `QUESTSUCCESSSTATE`).

Depends on: dialogue plan Phase 4 (quest commands) for the primary way quests start and advance; inventory plan for item objectives/rewards.

## Where we are

- `Quest` has id, title, description, a `SideQuest` flag, prerequisite quest flags, and a success state (`Unstarted`/`InProgress`/`Success`/`Failed`).
- `QuestStage` has stage number/subtitle/description; `QuestStagePrereq` is an empty stub.
- Nothing loads, tracks, or displays quests.

---

## Phase 1 — Quest data model and catalog

1. Split **definition** from **progress** (same pattern as items):
   - Definitions: `Quest` + ordered `QuestStage` list + prereqs, authored in a `res://Data/quests.json` catalog keyed by quest id. Move `SuccessState` **off** `Quest` — it's progress, not definition.
   - Progress: a `QuestProgress` record (`QuestId`, `QUESTSUCCESSSTATE`, `CurrentStageNumber`) stored in a `List<QuestProgress>` on `GameState`.
2. Flesh out stages: give `QuestStage` an **objective** description and an optional completion condition type (manual/scripted, item-in-inventory, npc-flag) — start with "scripted only" (advanced explicitly by dialogue/trigger commands) and add auto-checked conditions later; delete or fill `QuestStagePrereq` accordingly.
3. `QuestCatalog` loader with validation (stage numbers contiguous, prereq ids exist).
4. Bump `SaveVersion` with a migration for the progress list.

**Done when:** unit tests cover catalog validation and progress round-trips through save/load.

## Phase 2 — QuestManager

1. `QuestManager` autoload (or a plain class owned alongside `SaveManager`) exposing:
   - `StartQuest(id)` — validates prereqs via `QuestPrereqFlag` against current progress states.
   - `AdvanceQuest(id, stage)` / `CompleteQuest(id)` / `FailQuest(id)`.
   - `GetState(id)` — powering the dialogue plan's `quest_state("id")` Yarn function.
   - Events: `QuestStarted`, `QuestStageChanged`, `QuestCompleted` (UI + reward hooks subscribe).
2. Wire the dialogue commands (`<<start_quest>>`, `<<advance_quest>>`) from the dialogue plan Phase 4 to these APIs.
3. **World triggers:** a `QuestTrigger` Area3D node (enter area → advance stage X of quest Y, once) for non-dialogue beats like "reach the cargo bay".

**Done when:** the fetch-quest loop from the dialogue plan drives quest state through `QuestManager` and survives save/load at every stage.

## Phase 3 — Journal UI and player feedback

1. **Journal tab** in `InGameMenu`: active quests (main/side sections per the `SideQuest` flag), each showing title, current stage subtitle + objective, and completed/failed lists.
2. **HUD notifications:** toast on quest started/stage advanced/completed (subscribe to `QuestManager` events); optional single "tracked quest" objective line on `PlayerHud`.
3. Mark quest-relevant NPCs (indicator over quest givers with an available quest — reads prereq-satisfied, unstarted quests targeting that NPC).

**Done when:** a player can follow the fetch quest end-to-end using only the journal and HUD cues.

## Phase 4 — Rewards and depth

1. **Rewards** in quest definitions: XP (build plan's `GrantXp`, applied to the whole party), items (inventory plan), currency. Granted on `QuestCompleted`.
2. **Auto-checked objectives:** item-count and npc-flag stage conditions evaluated on relevant events (inventory changed, flag set) rather than per-frame polling.
3. **Branching outcomes:** quests completable in multiple ways — model as stages with alternate next-stage transitions; `Failed` state paths (e.g. quest NPC leaves) authored via dialogue/trigger commands.
4. Multi-quest chains via existing `QuestPrereqFlag` (e.g. side quest unlocked only after main quest 3 `Success`).

**Done when:** completing a quest grants XP/items, and one authored quest has two distinct completion paths.

## Decisions to settle early

| Decision | Recommendation |
|----------|----------------|
| Where quest logic lives | Definitions are data; transitions happen only through `QuestManager` APIs, driven by dialogue commands and triggers — no quest-specific C# per quest. |
| Stage progression shape | Linear stage list with explicit scripted advancement first; add conditions/branching in Phase 4 only once real content demands it. |
| Journal scope | Text-only journal for v1 — no map markers until a map system exists. *(The map now exists; markers are planned in [quest-markers.md](quest-markers.md), which also adds the target locations and the tracked quest the journal needs for objective lines.)* |
