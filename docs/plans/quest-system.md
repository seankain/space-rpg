# Quest System — Implementation Plan

Goal: main and side quests with stages, prerequisites, a journal UI, and completion rewards — built on the existing stubs in `Scripts/Data/Quest.cs` (`Quest`, `QuestStage`, `QuestPrereqFlag`, `QUESTSUCCESSSTATE`).

Depends on: dialogue plan Phase 4 (quest commands) for the primary way quests start and advance — **done**, `set_quest`/`advance_quest` are shipped `DialogueEffects` verbs; inventory plan for item objectives/rewards — the inventory itself is done, quest-facing rewards are not.

## Where we are

*(Reviewed against the tree at `872c474`, then updated as each phase landed.)* The plan's own phases have been worked on out of order: the parts the [quest-markers plan](quest-markers.md) needed were built first. **All four phases are in.** Quests start, advance, complete, save, display, run on authored stages, move through one place that reads prerequisites, show themselves without a menu, pay out on their own, and can be finished more than one way.

What exists:

- **Definitions** are `Quest` in a static `QuestCatalog` (`Id`, `Title`, `Description`, `SideQuest`, `PrereqQuests`, `Stages`, `Markers`), keyed by id, mirroring `ItemCatalog`. Two quests ship: "Return the Maguffin" (main) and "Clear the Deck" (side), two stages each.
- **Progress** is `QuestProgress` (`QuestId`, `State`, `CurrentStageNumber`) in `GameState.Quests`, saved since save version 3, moved only through `GameState.SetQuestState` / `SetQuestStage` / `AdvanceQuestStage`.
- **Transitions** all run through `QuestManager` (`StartQuest`, `SetState`, `CompleteQuest`, `FailQuest`, `AdvanceState`, `SetStage`, `AdvanceStage`, `ReachStage`), whoever asked for them: dialogue (`<<set_quest 1 Success>>`, `<<advance_quest 1>>`, `<<set_stage 1 next>>`), the developer console (`quest start|set|stage|advance`, `quests`), and world beats (`QuestTrigger`, a quest-carrying `Pickup`). The manager checks prerequisites, writes the `GameEventKind.Quest` log line with `notify` set — so any move raises a corner toast through `EventToasts` — and announces itself on the static `QuestManager.Moved` hook.
- **Reads** are `GameState.GetQuestState` / `GetQuestStage` / `GetCurrentStage`, exposed to authoring as the `quest_state(1, "Success")` predicate and the `quest(1)` / `quest_stage(1)` queries.
- **Journal**: `QuestLogMenu` drives the Quests tab — Main / Side / Completed / Failed sections, a details panel with the current stage's subtitle, an objective list, a Track toggle, and Show on Map.
- **In the world**: `QuestHud` (autoload) draws the tracked quest's title and current objective in the bottom-left corner while the player is playing, and `QuestGiverIndicator` floats a "!" over an NPC who has a quest to offer. Both read the engine-free `QuestObjectives` / `QuestManager.AvailableFrom`, and the link they need — `Quest.GiverNpcId` — is authored on the definition.
- **Markers and tracking**: `Quest.Markers`, `QuestMarkerResolver`, `QuestTargetLocator`, `QuestMarkerPlacements`, `GameState.TrackedQuestId` (save version 10) and the static `QuestTracking.Changed` hook — all four phases of [quest-markers.md](quest-markers.md), which is where the journal's objective lines come from.
- **Catalog validation** is `QuestCatalog.Problems`, run over every definition in the static constructor and reported as one message: bad markers, stage numbers that don't run 1..n in order, a stage with no subtitle, prerequisites that name a missing quest, the quest itself, or the same quest twice, and a giver id that couldn't be an NpcId.

- **A stage moves three ways**: something says so (`set_stage`, `quest stage`), the player walks into the place it stands for (`QuestTrigger`, an Area3D asking for "at least stage n"), or it reaches itself because its own `ReachedWhen` condition holds — re-checked by `QuestObjectiveWatcher` whenever the party does anything worth logging. All three are idempotent and forward-only, so nothing stores a fired flag in the save.
- **Rewards** are `Quest.Reward` (credits, party-wide XP, items), handed over by `QuestManager` the moment a quest succeeds, whichever route finished it.

What the plan does *not* cover, and is worth knowing:

- **Prerequisites have no content.** They are validated at registration and enforced by `StartQuest`, but no shipped quest declares one, so the machinery is exercised only by tests. The first authored chain is a content decision, not a missing mechanism.
- **Definitions are still C#.** `QuestCatalog` is a static registry; JSON authoring is Phase 1's one outstanding item, and markers, stages and rewards move with the definitions when it happens.
- **The journal shows a stage and its markers separately.** They agree for the shipped content because both are authored from the same beats; a quest with more beats than markers will show the difference.

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
3. ~~**World triggers**~~ — **done** as `QuestTrigger` (`Scripts/World/`): an Area3D that asks for "at least stage n" on a quest in progress when the player walks in. Idempotent by construction — walking back through, or reloading a save made afterwards, moves nothing — so no trigger stores a fired flag in the save. The stage is named rather than "the next one", because a trigger is a fixed place tied to a specific beat. `Pickup` briefly carried the same two fields for beats that are "you have the thing"; Phase 4 removed them again, because a stage condition says that in any order and this shape could not.
   - **The gap that showed up here:** a beat only fires while the player is standing in it, so picking the cube up before taking the quest never tripped its stage. That is what Phase 4 item 2 fixes for beats that are states; a trigger remains right for beats that are places.

**Done when:** the fetch-quest loop from the dialogue plan drives quest state through `QuestManager` and survives save/load at every stage. ***Met*** — Hale's conversation starts it, holding the cube reaches its second beat, his turn-in completes it and pays for it, every step through the manager, and `Tests/QuestStageTests.cs` covers the stage half of the round trip.

## Phase 3 — Journal UI and player feedback *(done)*

1. ~~**Journal tab** in `InGameMenu`~~ — **done** (`QuestLogMenu`): active quests in Main / Side sections per the `SideQuest` flag, plus Completed and Failed, each row showing title, and a details panel with status, description, and — for a quest still in progress — a "Current: …" line carrying the stage subtitle. The objective lines below it come from the markers plan's resolver, not from stages; the two say the same thing for the shipped content and will diverge once a quest has more beats than markers.
2. ~~**HUD notifications**~~ — **done, by two routes**. Quest moves are recorded as `GameEventKind.Quest` entries with `notify` set, and the generic `EventToasts` autoload turns any notifying entry into a corner toast, so there is no quest-specific toast subscription to write. The standing objective line is `QuestHud`, a second autoload built in code the way the toasts are: the tracked quest's title and its current objective, bottom-left, hidden whenever gameplay is blocked (a conversation, a menu, a battle).
   - Not on `PlayerHud`, where this list expected it: that shell is a `Control` in `Scenes/` that nothing instances, and the objective line needs its own `CanvasLayer` to sit under the dialogue box. `PlayerHud` is left for player vitals, which is what a HUD attached to the player scene is for.
   - What it says is `QuestObjectives`, engine-free: the current stage's subtitle when the quest has stages, else its live marker labels. The journal has room for both and shows both; the HUD has one line, so the choice had to live somewhere testable rather than being worded a third way in a third scene.
   - It follows `QuestManager.Moved` and `QuestTracking.Changed`, and compares tracked-quest/stage/blocked per frame for the two cases with no signal — a save loaded under it, and a window opening over it.
3. ~~Mark quest-relevant NPCs~~ — **done**: `Quest.GiverNpcId` is the missing link, authored on the definition (Hale gives the fetch quest, Marlow the bounty), validated at registration for shape and checked against the `.tres` files on disk by the test suite the way marker NPC targets are. `QuestManager.AvailableFrom(state, npcId)` answers "what can this NPC offer right now" — authored giver, not started, prerequisites met — and `QuestGiverIndicator`, added to every spawned NPC, shows a "!" while that list isn't empty and refreshes on every quest move.
   - The mark only ever says "there is something here", never which quest: a title floating over a stranger's head gives away a beat the conversation should land.
   - The link is authored rather than derived from a conversation's `set_quest` verbs — dialogue moves between NPCs freely, and a quest is offered by whoever the writer says offers it.

**Done when:** a player can follow the fetch quest end-to-end using only the journal and HUD cues. ***Met*** — the "!" over Hale points at the quest before it exists, the HUD line carries the current objective through the middle of it, toasts announce each move, and the journal and map are there for the detail.

## Phase 4 — Rewards and depth *(done)*

1. ~~**Rewards** in quest definitions~~ — **done** as `Quest.Reward`: credits, experience granted to every party member the way a battle's is, and a list of `ItemStack`s. Validated at registration (an item that doesn't exist, a quantity of zero, or a reward object that grants nothing at all), and handed over by `QuestManager` the moment the quest's state reaches `Success`.
   - Granted **inside the transition** rather than by a subscriber to `Moved`, which is where this plan expected the hook. Transitions already funnel through one place; putting the payout there means it can't be missed by an ordering nobody thought about, and a subscriber still sees a quest that has already paid.
   - Each part logs the line that gain always logs — "Received Maintenance Keycard.", "Earned 40 credits.", "The party gains 15 XP." — so the log doesn't grow a second vocabulary for being given something.
   - Content: "Clear the Deck" pays the keycard that used to be a `give_item` in Marlow's turn-in line, plus credits and XP; "Return the Maguffin" pays credits and XP, where it used to pay nothing at all. The XP is written straight onto `CharacterEntity.ExperiencePoints`, exactly as `BattleScene` does on victory — levelling from it is still the build plan's business.
2. ~~**Auto-checked objectives**~~ — **done** as `QuestStage.ReachedWhen`, a `ConditionRef` from the same vocabulary markers and dialogue branches use, so there is one condition language and the editor's form already renders it. `QuestObjectiveWatcher` re-evaluates them and walks each in-progress quest as far as its conditions allow.
   - Driven by `GameEventLog.Recorded` rather than per-frame polling or a new signal per kind of state: every gain, purchase, battle and conversation the player has already records an entry, which is exactly the set of moments worth re-asking after. `SaveManager` also evaluates once on load, so a save made before a condition existed catches up.
   - Forward-only, one stage at a time, and a stage with **no** condition stops the walk — a beat something has to announce can't be stepped over by the one after it happening to be true.
   - This is what closes Phase 2's known ordering gap: "Return the Maguffin" stage 2 is now `has_item`, true whenever it is asked, so picking the cube up before taking the quest reaches the beat anyway. The `Pickup` quest fields added alongside `QuestTrigger` are **removed** — a condition says the same thing in any order, and two ways to express one beat is one too many. `QuestTrigger` stays: "reach the cargo bay" is a place, not a state.
3. ~~**Branching outcomes**~~ — **done** for "Clear the Deck", which now has two routes to its second beat and two turn-ins to match. Beating Vex reaches stage 2 by condition (`npc_defeated`); paying him 120 credits to move two decks down reaches it by `set_stage` from his own conversation, and sets the flag everyone else reads. Marlow's turn-in branches on the same two facts and has a different opinion of each.
   - The turn-in reads **the world** (`npc_defeated`, `flag("vex_paid")`) rather than the stage both routes converge on. A stage is a summary for the journal and the HUD; a conversation that won't take your turn-in because a summary is stale is the worst bug this system could have.
   - The reward moving onto the definition is what makes a second route possible at all: the keycard used to live in one turn-in line, and the other route would have finished the quest without it.
   - `Failed` paths are still unauthored. Hale's refusal already ends in a fight that leaves the quest line intact, and nothing in the shipped content wants a quest that can be lost.
4. **Multi-quest chains** via `QuestPrereqFlag` — **mechanism done** (validated at registration, enforced by `QuestManager.StartQuest`, and `PrereqProblem` is what the quest-giver indicator asks), **unexercised by content**: no shipped quest declares a prerequisite. Authoring one is a content decision and needs no more code.

**Done when:** completing a quest grants XP/items, and one authored quest has two distinct completion paths. ***Met*** — both shipped quests pay out on completion, and "Clear the Deck" can be finished by force or by money, with a different turn-in for each.

## Suggested next slice

All four phases are in, so what follows is either content or a plan of its own:

- **JSON authoring for `QuestCatalog`** (Phase 1 item 1) — the last mechanism this plan named and didn't build. Markers, stages and rewards move with the definitions.
- **A quest chain** — the prerequisite machinery has never been exercised by content, and one authored chain is what would prove it.
- **Rewards that need other plans**: XP that levels a character (build plan's `GrantXp`), and a `Failed` path worth authoring.

## Decisions to settle early

| Decision | Recommendation |
|----------|----------------|
| Where quest logic lives | Definitions are data; transitions happen only through `QuestManager` APIs, driven by dialogue commands and triggers — no quest-specific C# per quest. *(Held, and now literally: `QuestManager` exists and every dialogue verb, console verb and world beat goes through it. Storage and tracking stay on `GameState`, which is what the markers plan relies on.)* |
| Stage progression shape | Linear stage list with explicit scripted advancement first; add conditions/branching in Phase 4 only once real content demands it. *(Held: `Quest.Stages` is a linear list numbered 1..n, moved only by `set_stage` / `quest stage`. The two vocabularies coexist as hoped — a stage is scripted, and `quest_stage(1) >= 2` gates a marker or a branch the same way any other condition does.)* |
| Journal scope | Text-only journal for v1 — no map markers until a map system exists. *(Superseded twice: the map exists and markers shipped — [quest-markers.md](quest-markers.md), all four phases — which is also where the journal's objective lines and the tracked quest came from; and the quest now reaches outside the menu entirely, as a HUD line and a mark over its giver's head.)* |
| Catalog format | Static C# registry for now, mirroring `ItemCatalog`; JSON authoring (Phase 1 item 1) is still wanted, and markers move with the definitions when it happens. |
