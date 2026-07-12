# Party System — Implementation Plan

Goal: the player leads a party of characters — recruitable, visible in the world, manageable in menus, and (eventually) fielded together in turn-based combat.

## Where we are

- `GameState.Party` is already a `List<CharacterEntity>` — the party has been the intended model from the first stub.
- `CharacterEntity` already carries everything a member needs (stats, level, HP/MP, equipment, status effects, position).
- Exactly one player is spawned today (by both `Level.cs` and `LevelManager` — the save/load plan already calls for consolidating spawning into `LevelManager` first).

---

## Phase 1 — Party roster model

1. `PartyManager` (plain class owned by `GameState` or the `SaveManager`-adjacent layer): ordered member list with a designated **leader** (index 0 = the controlled character), `AddMember`, `RemoveMember`, `SetLeader`, and a max size (recommend 4).
2. Distinguish **party members** from the **recruitable pool**: recruitable characters are defined in game data (like the item catalog); recruiting copies the definition into `GameState.Party` as a live `CharacterEntity`. Benched members (roster beyond the active four) can be deferred until content needs it.
3. Party creation at New Game produces a single-member party (the build plan's Phase 2 character).

**Done when:** unit tests cover add/remove/leader rules; a multi-member party round-trips through save/load.

## Phase 2 — Party in the world (followers)

1. A `PartyMemberFollower` scene: same character body/animation setup as `Player.tscn` but AI-driven — follows the leader with spacing (Godot `NavigationAgent3D`, or simple follow-the-leader breadcrumb trail to start; the Intro level needs a `NavigationRegion3D` bake if using navmesh).
2. `LevelManager` spawns the leader as the controlled `Player` and one follower per remaining member when a level loads/restores — this extends the save/load plan's Phase 2 restore flow, which is why spawn-ownership consolidation must land first.
3. Follower positions saved via each member's existing `CharacterEntity.Position` on capture.
4. Leader switching (optional this phase): swap which member is player-controlled; others become followers.

**Done when:** a two-member party walks through the Intro level together and reloads correctly from a save.

## Phase 3 — Party in menus

1. Party tab in `InGameMenu`: member list with portraits/HP/MP, tap into each member's character sheet (reuses the build plan's Phase 3 sheet — parameterize it by `CharacterEntity` now if not already).
2. Equipment per member via the inventory plan's equipment pane.
3. Reordering members (sets leader and future combat turn layout).
4. Recruit/dismiss flow: triggered by dialogue or quest events (Yarn command `<<recruit CharacterId>>` — see dialogue plan Phase 4), with a confirmation UI.

**Done when:** every member's sheet and equipment are reachable from Tab, and a scripted NPC can join the party.

## Phase 4 — Combat and content hooks

Contracts for later systems:

- **Turn-based combat** fields the active party; turn order from each member's Dexterity-derived stat. Combat reads/writes members' HP/MP/status directly on `CharacterEntity` so results persist automatically.
- **KO/death policy:** decide when combat lands (recommend: KO'd members revive at 1 HP after combat — avoids permadeath save-scumming design early).
- **Dialogue** can branch on party composition (Yarn variable bridge, dialogue plan Phase 3).
- New party-related state (bench, affinity, formation) extends `GameState` under the save plan's Phase 5 versioning contract.

## Decisions to settle early

| Decision | Recommendation |
|----------|----------------|
| Max active party size | 4 — fits typical turn-based encounter design and follower pathing cost. |
| Followers physical or cosmetic | Physical (`CharacterBody3D`) but non-blocking vs the player — cheap and avoids "follower stuck in doorway" blocking bugs. |
| Shared vs per-member inventory | Shared (matches inventory plan); equipment per member. |
