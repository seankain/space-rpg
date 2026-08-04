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

## Phase 1 — Marker data model and resolver *(engine-free, no UI)*

1. `Scripts/Data/QuestMarker.cs` — engine-free, so `Tests/SpaceRpg.Tests.csproj` compiles it through the existing `Scripts/Data/**` glob:
   - `QuestMarkerTarget`: a kind + reference — `Npc` (`NpcDefinition.NpcId`, e.g. `intro.dockmaster_hale`), `Item` (an `ItemCatalog` id, meaning "the world pickup for this item"), `Point` (an area name plus world X/Z, for "reach the cargo bay" objectives with nothing standing there).
   - `QuestMarker`: target, a short `Label` ("Find the Maguffin Cube"), and an optional `VisibleWhen` `ConditionRef`.
   - `Quest.Markers` — an ordered `List<QuestMarker>` on the definition.
2. `Scripts/Data/QuestMarkerResolver.cs` — given a `GameState` and a quest id, returns the markers that apply: quest must be `InProgress`, and each marker's `VisibleWhen` must evaluate true. Condition evaluation goes through `DialogueConditions` against a state-only context (add `DialogueContext.ForState(state)` — every verb a marker may use reads `GameState` alone).
3. Validation at catalog registration: unknown target kind, empty reference, a condition that fails `DialogueConditions.Validate`, or a condition verb outside the state-only subset (anything needing a live NPC or scene host) throws at startup rather than silently hiding a marker.
4. Author markers for the two shipped quests:
   - **Return the Maguffin** — `Item:1` labelled "Find the Maguffin Cube" while `!has_item:1`; `Npc:intro.dockmaster_hale` labelled "Return the cube to Dockmaster Hale" while `has_item:1`.
   - **Clear the Deck** — `Npc:intro.vex` ("Deal with Vex") while `!npc_defeated:intro.vex`; `Npc:intro.chief_marlow` ("Report back to Chief Marlow") while `npc_defeated:intro.vex`.
5. Resolution to a *position* is a port, not a dependency: `IQuestTargetLocator` (`TryLocate(target) → area name + world X/Z`), so the resolver and its tests stay engine-free and Phase 2 supplies the Godot implementation.

**Done when:** unit tests cover the fetch quest flipping its marker as the cube enters the inventory, an un-started/completed quest resolving to nothing, a bad marker failing catalog validation, and an unlocatable target being dropped rather than throwing.

## Phase 2 — Locating targets: live NPC data and baked world points

1. `Scripts/World/QuestTargetLocator.cs` — the Godot-side `IQuestTargetLocator`:
   - **Npc** → `NpcDatabase.Get(npcId)`; position is `ChunkCoords·64 + LocalPosition` through `MapProjection`, area from the definition's `SpawnScenePath`. If the NPC is currently in the scene tree, its live `GlobalPosition` wins.
   - **Item** → the baked pickup index (below).
   - **Point** → the authored coordinates as-is.
2. Extend the bake to record what a marker can point at, keeping the "scene-authored things are baked, data-owned things are read live" split:
   - `MapBakeJob.CollectLandmarks` also records every `Pickup` (type `pickup`, carrying its `ItemId`) and any `QuestPoint` node (a trivial `Node3D` marker script authored in a chunk, carrying a name) it walks past.
   - `MapLandmark` gains `ItemId` and `TargetScenePath`; `MapLandmarksFile.CurrentVersion` → 2. Door entries record their `TargetScenePath`, which is what Phase 4's interior routing matches against.
   - Pickup/quest-point entries are **not** drawn as ordinary landmarks — the map reads them only when a quest marker asks for one. `MapMenu.AddBakedLandmarks` filters by type instead of drawing everything in the file.
3. Re-bake and commit `Resources/Maps/**` for `IntroStation` and `World1`; `WorldMapBakeFreshnessTests` will demand it once chunk hashes are re-written.
4. Round-trip tests for the extended manifest (engine-free, alongside the existing `MapLandmarkTests`), plus a test that every marker authored in `QuestCatalog` names a target that exists in `NpcDatabase`/`ItemCatalog` — the guard against a renamed NPC id silently killing a marker.

**Done when:** a small harness (a console verb, `quest markers <id>`) prints the resolved world positions for a quest's live markers, correct for both the cube on the deck and Hale at his post, with every chunk unloaded.

## Phase 3 — Tracking a quest from the Quests tab

1. `GameState.TrackedQuestId` (`uint`, `0` = none) — save version 10; pre-v10 saves load with nothing tracked (property default, no migration step). Set through `GameState.SetTrackedQuest(id)`, which refuses a quest that is not `InProgress` and clears itself when the tracked quest leaves `InProgress` (so completing a quest retires its markers — hook the existing quest-state move in `DialogueEffects.MoveQuest`).
2. `QuestLogMenu`: selecting an in-progress quest tracks it. The details panel gains
   - an objective line listing the current marker labels (from the Phase 1 resolver — the first thing the journal has said about *what to do next*),
   - a **Track / Tracked** toggle for untracking without leaving the tab,
   - a **Show on Map** button, which switches the parent `TabContainer` to the Map tab.
   `Refresh` re-selects the tracked quest instead of clearing the selection, so reopening the menu shows what the player is following.
3. A static `QuestTracking.Changed` hook (the `GameEventLog.Recorded` pattern — `GameState` instances are swapped out on load, so per-instance subscriptions go stale) for the Map tab and any future HUD.
4. Console: `quest track <id>` / `quest untrack` in `EditorQuestCommands`, and the tracked quest shown in `quests` output. Engine-free, tested with the sibling verbs.

**Done when:** selecting a quest and saving/reloading comes back with the same quest tracked and the same objective lines, and completing a tracked quest clears the tracking.

## Phase 4 — Markers on the map

1. `Scripts/QuestMarkerIcon.cs` — a `Control` with `_Draw` beside `MapLandmarkIcon`: a pointed diamond over a dark backing disc, gold for a main quest and pale blue for a side quest, tooltip = the marker's label. `MouseFilter.Pass`, like the landmark icons, so pan/zoom still reach the map underneath.
2. `MapMenu.BuildQuestMarkers`, run after `BuildLandmarks` so markers sit above landmarks and below the player arrow. It resolves the tracked quest through the Phase 1 resolver + Phase 2 locator and places each marker with `MapProjection`. Icons join the existing counter-scale list so they keep a constant on-screen size at any zoom (rename `landmarkIcons` → `mapIcons`).
3. Targets that are not in the current area's grid:
   - **Inside an interior reachable from here** (the shop, and any future house): match the target's area/scene path against the baked door landmarks' `TargetScenePath` and draw the marker on that **door**, labelled "Trader Moss — inside the Supply Shop". This is the routing case that matters today.
   - **In a different area entirely:** no icon; the map header shows "Objective: <label> — <area>".
4. The header carries the tracked quest's title and the current objective labels, so the map answers "what am I doing" without flipping back to the Quests tab. **Show on Map** (Phase 3) centres the view on the nearest resolvable marker rather than on the player.
5. Refresh on `QuestTracking.Changed` as well as `VisibilityChanged` — tab switching already re-runs `Refresh`, but the hook keeps the map honest if tracking changes while it is up (console verb, or a quest completing).

**Done when:** taking "Return the Maguffin", selecting it in the Quests tab, and opening the Map shows a gold marker on the cube's spot; picking the cube up and reopening the map moves the marker to Hale; tracking "Clear the Deck" shows a blue marker on Vex; nothing is tracked → the map looks exactly as it does today.

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
| Target positions | Split by source of truth, exactly like landmarks: NPCs resolve live through `NpcDatabase` (never stale after an NPC is moved), scene-authored pickups and quest points are collected at bake time (their chunks are usually unloaded). |
| Tracked-quest scope | One tracked quest, persisted in `GameState` (save v10). Drawing every active quest at once clutters the map and makes "which marker is which" the UI's problem before there is content to justify it. |
| Selection vs. explicit tracking | Selecting an in-progress quest tracks it (what the feature request asks for), with a Track toggle to undo and **Show on Map** to jump. Completed/failed selections never track. |
| Interior targets | Route to the entrance door via the baked `TargetScenePath` rather than drawing a marker at coordinates the current map doesn't cover. "Go through this door" is the actionable instruction. |
| Icon assets | Drawn in `_Draw` like every other map glyph — consistent with `MapLandmarkIcon`, no assets to theme, tooltips for free. |
| Unresolvable markers | Dropped silently on the map with a header note, and caught at test time by a catalog-integrity test — a content bug should fail the suite, not the play session. |
