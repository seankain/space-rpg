# Quest Markers — Implementation Plan

Goal: selecting an active quest in the in-game menu's **Quests** tab marks its current objectives on the **Map** tab — an icon on the Maguffin Cube while it is still lying on the deck, on Dockmaster Hale once it is in the party's pockets. Markers follow quest progress without a stage system existing yet, and they resolve without the target's chunk being loaded.

Depends on: [world-map.md](world-map.md) Phases 1–3 (baked chunk images, `MapProjection`, `MapMenu`, `MapLandmarkIcon`) — all done. Picks up world-map Phase 5's "Quest markers" stretch item and the quest plan's deferred "no map markers until a map system exists" decision.

## Where we are

- `QuestLogMenu` drives the Quests tab: a sectioned `ItemList` (Main / Side / Completed / Failed) over `GameState.Quests`, with a details panel showing title, status, description. Selection is already plumbed (`OnQuestSelected`) and today only repaints the details panel.
- `MapMenu` builds the Map tab in code: a pannable/zoomable `world` layer holding one `TextureRect` per baked chunk, landmark icons, and a player arrow, rebuilt on every `VisibilityChanged`. Icons are `Control`s drawn in `_Draw` (`MapLandmarkIcon`), counter-scaled against zoom, with hover tooltips.
- `MapProjection` is the single world↔pixel helper shared by the bake tool and the map UI. Engine-free, unit-tested.
- Landmarks come from two sources by design: scene-authored portals/doors baked into `Resources/Maps/<Area>/landmarks.json` by `MapBakeJob`, and shopkeepers read live from `NpcDatabase` (which knows every NPC's `SpawnScenePath`, `ChunkCoords`, and `LocalPosition` without loading a chunk).
- `Quest` is definition-only (id, title, description, `SideQuest`, prereqs); progress is `QuestProgress` in `GameState.Quests`. **There are no stages and no target locations anywhere** — this plan adds the location half.
- There is a fixed, engine-free, tested condition vocabulary (`DialogueConditions` + `DialogueQueries`: `has_item`, `quest_state`, `npc_defeated`, `flag`, `item_count`, …) evaluated against `GameState`. Quest markers reuse it rather than inventing a second one.
- Chunks stream: at any moment most of an area is not in the scene tree. Anything a marker needs must come from baked data or from `NpcDatabase`, never from live nodes only.

## Conventions

- **Markers are quest data, not scene data.** A marker names a *target* (an NPC id, an item id, a world point) plus the condition under which it is relevant. Positions are resolved at display time from whichever registry owns that target's truth — the same source-of-truth split the map already uses for landmarks vs. shopkeepers.
- **One tracked quest.** Selecting an in-progress quest tracks it; the map draws that quest's markers. Tracking lives in `GameState`, so it survives save/load and is available to a later HUD compass or minimap.
- **Visibility is a condition, not a stage.** `QuestMarker.VisibleWhen` is a `ConditionRef` from the existing dialogue vocabulary, restricted to the state-only verbs. This is what lets "Return the Maguffin" flip from cube to Hale today, and it stays correct when stages land (a stage-number condition becomes one more verb).
- **Icons drawn in code**, like the player arrow and the landmark glyphs: no binary assets to bake or theme, and a `Control` gets its objective text as a hover tooltip for free.
- **Never depend on a loaded chunk.** A marker on an NPC uses `NpcDefinition`'s authored spawn position; if that NPC happens to be in the scene tree (wandering, patrolling), its live `GlobalPosition` is preferred, but absence is normal, not an error.
- **Unresolvable is silent.** A target in another area, a deleted NPC, an un-baked pickup: the marker is dropped with a one-line explanation in the map header, never an exception.

---

## Phase 1 — Marker data model and resolver *(done — this slice; engine-free, no UI)*

1. `Scripts/Data/QuestMarker.cs` — engine-free, so `Tests/SpaceRpg.Tests.csproj` compiles it through the existing `Scripts/Data/**` glob:
   - `QuestMarkerTarget`: a kind + reference — `Npc` (`NpcDefinition.NpcId`, e.g. `intro.dockmaster_hale`), `Item` (an `ItemCatalog` id, meaning "the world pickup for this item"), `Point` (an area name plus world X/Z, for "reach the cargo bay" objectives with nothing standing there). Serialized as a flat colon token (`npc:intro.vex`, `item:1`, `point:IntroStation:64:-12.5`), the `TokenRef` habit the dialogue vocabulary set, so the catalog can move to JSON later without a nested shape.
   - `QuestMarker`: target, a short `Label` ("Find the Maguffin Cube"), and an optional `VisibleWhen` `ConditionRef`.
   - `Quest.Markers` — an ordered `List<QuestMarker>` on the definition.
2. `Scripts/Data/QuestMarkerResolver.cs` — `ActiveMarkers(state, quest)` returns the markers that apply: quest must be `InProgress`, and each marker's `VisibleWhen` must evaluate true. Conditions go through `DialogueConditions` against a `DialogueContext` carrying `State` and a warning sink and **nothing else** — no speaking NPC, no scene host — so a verb that ever needs either fails closed (warns, hides the marker) exactly as it does mid-conversation. Every verb in today's vocabulary reads `GameState` alone, so no allow-list is needed to get that property. Overloads by quest id and by `Quest` (a caller that already holds the definition, and the seam a test injects broken content through).
3. Validation at catalog registration: an unknown target kind, an empty or colon-bearing reference, an item id with no catalog entry, a missing label, or a condition that fails `DialogueConditions.Validate` throws — run once after every `Register`, so a marker condition may name any quest without tripping over a half-filled catalog. The resolver re-checks and skips rather than throwing, since a future data-authored catalog can hand it content the registration path never saw.
4. Author markers for the two shipped quests:
   - **Return the Maguffin** — `Item:1` labelled "Find the Maguffin Cube" while `!has_item:1`; `Npc:intro.dockmaster_hale` labelled "Return the cube to Dockmaster Hale" while `has_item:1`.
   - **Clear the Deck** — `Npc:intro.vex` ("Deal with Vex") while `!npc_defeated:intro.vex`; `Npc:intro.chief_marlow` ("Report back to Chief Marlow") while `npc_defeated:intro.vex`.
5. Resolution to a *position* is a port, not a dependency: `IQuestTargetLocator` (`TryLocate(target) → QuestTargetLocation`: area name, level scene path, world X/Z), so the resolver and its tests stay engine-free and Phase 2 supplies the Godot implementation. `Resolve(...)` pairs each active marker with its location as a `ResolvedQuestMarker` (carrying the quest id and its `SideQuest` flag, so the map can colour markers without a second catalog lookup) and drops what the locator can't place.

**Done when:** unit tests cover the fetch quest flipping its marker as the cube enters the inventory, an un-started/completed quest resolving to nothing, a bad marker failing catalog validation, and an unlocatable target being dropped rather than throwing. *(`Tests/QuestMarkerTests.cs`, 26 cases — the two shipped quests both ways round, target-token round-trips and the malformed forms, every rejection reason, and the fail-closed skip.)*

## Phase 2 — Locating targets: live NPC data and baked world points *(done — this slice)*

1. `Scripts/World/QuestTargetLocator.cs` — the Godot-side `IQuestTargetLocator`. **The live world outranks recorded data wherever it can reach**, which is both more accurate and what makes the feature demonstrable before anything is re-baked:
   - **Npc** → the spawned `Npc` node's own `GlobalPosition` when it is in the tree (they wander and patrol, so the authored spawn point is only an approximation), else `NpcDatabase.Get(npcId)` and `ChunkCoords·64 + LocalPosition` through `MapProjection` — readable with every chunk unloaded.
   - **Item** → the live `Pickup` node when its chunk is streamed in (a collected pickup is *gone*, so the marker goes with it), else the position the bake recorded.
   - **Point** → the authored coordinates as-is; it names its own area, so it resolves even for an area that isn't running.
   - `ForCurrentLevel()` resolves the area through `ChunkManager.FindIn(LevelManager.Instance.LevelRoot)`. An interior has no `ChunkManager`: it still gets a locator (live NPCs and pickups are findable) with no area name, so baked lookups are skipped rather than aimed at the wrong area.
2. Extend the bake to record what a marker can point at, keeping the "scene-authored things are baked, data-owned things are read live" split:
   - `MapBakeJob.CollectLandmarks` also records every `Pickup` (type `pickup`, carrying its `ItemId` and the item's catalog name).
   - `MapLandmark` gains `ItemId` and `TargetScenePath`; `MapLandmarksFile.CurrentVersion` → 2, with `FindPickup(itemId)` and `FindEntranceTo(scenePath)` as the two lookups its readers actually want. Doors and portals record their `TargetScenePath`, which is what Phase 4's interior routing matches against. A v1 manifest still loads — it simply has no pickups.
   - Pickup entries are **not** drawn as ordinary landmarks: `MapMenu.AddBakedLandmarks` filters to portals and doors, so a manifest entry is a fact about the world rather than an icon by itself.
   - Parsing the manifest moved out of `MapMenu` into `MapLandmarkCatalog` (cached per area, `Invalidate()`, missing file reads as empty) now that the map UI and the locator both read it.
3. Two small seams the locator needs: `Npc` and `Pickup` join node groups (`Npc.GroupName`, `Pickup.GroupName`) so "where is this right now" is a group query rather than a tree walk, and `ChunkManager` exposes `AreaName`, `LevelScenePath()`, and the `FindIn` walk that `MapMenu` had a private copy of.
4. Round-trip tests for the extended manifest, `quest markers` command tests against a fake locator, and a content guard that every `npc:` marker target names a definition under `Resources/Npcs` — read off disk, since `NpcDatabase` is Godot-facing. That is the check that catches a renamed NPC id silently killing a marker.

**Done when:** `quest markers <id>` prints the resolved world positions for a quest's live markers, correct for both the cube on the deck and Hale at his post. *(Done. Note the repo has **no committed bake at all** — `Resources/Maps/` does not exist — so today every target resolves through the live path, which covers the intro station where both quests play out. Item targets in an unloaded chunk stay unresolvable until someone runs Project → Tools → Bake World Maps and commits the output; NPC targets never needed it.)*

## Phase 3 — Tracking a quest from the Quests tab *(done — this slice)*

1. `GameState.TrackedQuestId` (`uint`, `0` = none) — save version 10; pre-v10 saves load with nothing tracked (property default, no migration step). Set through `GameState.SetTrackedQuest(id)`, which refuses a quest that is not `InProgress` and returns whether it took.
   **Tracking follows the quest log by itself**, all of it in `GameState.SetQuestState` rather than in `DialogueEffects.MoveQuest`, so every path a quest can move by — dialogue effects, console verbs, a future `QuestManager` — behaves the same:
   - taking a quest while following none follows it (a second one never steals the player's choice);
   - finishing the tracked quest hands over to another still in progress — main quests before side, the journal's own order — and clears tracking when that was the last one.
   Requiring a click in the journal before anything appeared on the map was the original Phase 3 behaviour, and it was wrong: a player who takes a quest and opens the map should see where to go, not an empty grid.
2. `QuestLogMenu`: selecting an in-progress quest tracks it (a completed one is only read, and leaves tracking alone). The details panel gains
   - an objective list from the Phase 1 resolver — the first thing the journal has said about *what to do next*,
   - a **Track / Tracked** button, a toggle — since tracking now starts on its own, there has to be a way to turn the markers off,
   - a **Show on Map** button, which switches the parent `TabContainer` to the tab whose control is the `MapMenu` (found by type, so reordering tabs can't send the player elsewhere).
   `Refresh` re-selects the tracked quest instead of clearing the selection, and the tracked row wears a `▸`. Re-labelling rows after tracking moves is deliberately *not* a `Refresh` — rebuilding re-selects, which re-tracks, which would loop.
   These three controls are built in code and appended to the details column the scene already declares, the same choice `MapMenu` makes for its whole tab; the `.tscn` is untouched.
3. A static `QuestTracking.Changed` hook (the `GameEventLog.Recorded` pattern — `GameState` instances are swapped out on load, so per-instance subscriptions go stale) for the Map tab and any future HUD. Nothing subscribes until Phase 4.
4. Console: `quest track <id>` / `quest untrack`, and `quests` marks the tracked quest with a leading `>`. Engine-free, tested with the sibling verbs.

**Done when:** selecting a quest and saving/reloading comes back with the same quest tracked and the same objective lines, and completing a tracked quest clears the tracking. *(Done — `Tests/QuestTrackingTests.cs` covers the rules, the change signal, the save round-trip, and a pre-v10 save loading untracked.)*

## Phase 4 — Markers on the map *(done — this slice)*

1. `Scripts/QuestMarkerIcon.cs` — a `Control` with `_Draw` beside `MapLandmarkIcon`: a diamond over a dark backing disc (a different *shape*, so an objective doesn't read as one more landmark), gold for a main quest and pale blue for a side quest, hollow when it marks the way to a target rather than the target itself. Tooltip = the objective text. `MouseFilter.Pass`, like the landmark icons, so pan/zoom still reach the map underneath.
2. **The routing decision is engine-free**, in `QuestMarkerPlacements` (`Scripts/Data/QuestMarkerPlacement.cs`), not in the UI: given the tracked quest, a locator, the area on screen, and the area's landmarks, it classifies every active marker as `OnMap`, `AtEntrance`, or `Elsewhere`, and composes the text to show. That is the part worth unit-testing; `MapMenu` only draws the answer.
   - **`AtEntrance`** is the case that matters in a world with interiors: the target is inside a level this map doesn't cover, and a door or portal here leads to it (matched on the `TargetScenePath` the Phase 2 bake records), so the marker goes on that door — "Trader Moss — inside Supply Shop".
   - **`Elsewhere`** covers another area, and also a target the locator couldn't place at all. No icon, but the objective line still names it ("— in World1", "— not on this map") rather than quietly showing one fewer objective.
3. `MapMenu.BuildQuestMarkers` runs after `BuildLandmarks` so an objective sits above the door or store it shares a spot with, and before the player arrow, which stays on top. Icons join the counter-scale list (`landmarkIcons` → `mapIcons`) so they keep a constant on-screen size at any zoom.
4. An objective line beside the area name carries the tracked quest's title and its current objectives, so the map answers "what am I doing" without flipping back to the Quests tab — and says "No quest tracked — pick one in the Quests tab" when a player with open quests has turned tracking off, rather than leaving them to wonder where the markers went. **Show on Map** (Phase 3) calls `FocusTrackedObjective()`, which frames the drawn objective *nearest the player* — and defers itself if it arrives before the tab has built, rather than being dropped.
5. Refresh on `QuestTracking.Changed` as well as `VisibilityChanged` — tab switching already re-runs `Refresh`, but the hook keeps the map honest if tracking changes while it is up (the console verb, or a quest completing). Marker positions are resolved per refresh, not per frame: the in-game menu blocks gameplay, so nothing can move while the map is open.

**Done when:** taking "Return the Maguffin", selecting it in the Quests tab, and opening the Map shows a gold marker on the cube's spot; picking the cube up and reopening the map moves the marker to Hale; tracking "Clear the Deck" shows a blue marker on Vex; nothing is tracked → the map looks exactly as it did before. *(Code complete and unit-tested at the placement layer; the on-screen check needs a Godot session, and the map itself stays an empty grid until someone bakes and commits `Resources/Maps/**`.)*

## Phase 5 — Beyond the map tab

1. **Off-screen indicators:** a marker outside the clipped viewport draws as an arrow clamped to the map's edge, pointing at it — the map is pannable, so an objective can easily sit out of frame at high zoom.
2. **World-space beacon:** a floating marker over the target in the 3D world when the player is within a chunk or two, reusing `InteractionPrompt`'s billboard/no-depth-test approach.
3. **HUD compass / minimap:** the tracked quest's nearest marker as an edge bearing on the minimap from world-map Phase 5 — the reason tracking lives in `GameState` rather than in the menu.
4. **Show all active quests** as a map toggle, once more than a handful of quests exist and one tracked quest stops being enough.
5. **Stage-driven markers:** when the quest plan's Phase 1 stages land, markers move from "conditions on state" to "markers per stage", with `VisibleWhen` kept for within-stage variation. The resolver's signature does not change.

## Decisions to settle early

| Decision | Recommendation |
|----------|----------------|
| Where marker data lives | On the quest definition in `QuestCatalog`, beside title/description. Markers are authored content, not scene content; when the catalog moves to JSON (quest plan Phase 1) they move with it. |
| Marker visibility before stages exist | Reuse `DialogueConditions` restricted to the state-only verbs. A second gating vocabulary would have to be validated, edited, and tested twice, and stages will slot in as one more verb. |
| Target positions | Split by source of truth, exactly like landmarks: NPCs resolve live through `NpcDatabase` (never stale after an NPC is moved), scene-authored pickups are collected at bake time (their chunks are usually unloaded). Within each kind, a node that is actually in the scene tree wins over the recorded position — it is the truth, and it also means an item that has been picked up stops being pointed at. |
| A `QuestPoint` node to place objectives visually | Not built. A `Point` target carries its own coordinates, so an authored point needs nothing from the bake; add the node only if placing one in the editor becomes the workflow. |
| Tracked-quest scope | One tracked quest, persisted in `GameState` (save v10). Drawing every active quest at once clutters the map and makes "which marker is which" the UI's problem before there is content to justify it. |
| Selection vs. explicit tracking | Selecting an in-progress quest tracks it (what the feature request asks for), with a Track toggle to undo and **Show on Map** to jump. Completed/failed selections never track. |
| Interior targets | Route to the entrance door via the baked `TargetScenePath` rather than drawing a marker at coordinates the current map doesn't cover. "Go through this door" is the actionable instruction. |
| Icon assets | Drawn in `_Draw` like every other map glyph — consistent with `MapLandmarkIcon`, no assets to theme, tooltips for free. |
| Unresolvable markers | Dropped silently on the map with a header note, and caught at test time by a catalog-integrity test — a content bug should fail the suite, not the play session. |
