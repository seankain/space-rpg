# NPC System — Implementation Plan

Goal: non-player characters that populate levels, can be interacted with (feeding the dialogue system), wander or hold posts, and whose state persists in saves.

## Where we are

- `Scenes/Npc.tscn` exists but has **no script** — it's a body/mesh shell.
- The `Interact` input action (E) is defined in `project.godot` but nothing listens for it.
- `CharacterEntity.ChunkId` anticipates per-area world state; the save/load plan's Phase 5 reserves a "world state" bucket for NPC data.

---

## Phase 1 — NPC base and interaction

1. `Npc.cs` on `Npc.tscn`: a `CharacterBody3D` (or `StaticBody3D` for fixed NPCs) with an exported `NpcId`, display name, and an `Area3D` **interaction zone**.
2. **Interaction plumbing** (shared with pickups from the inventory plan): an `IInteractable` interface (`InteractionPrompt`, `Interact(Player)`); `Player` tracks the nearest interactable in range and fires it on the `Interact` action. Show a "[E] Talk" prompt on `PlayerHud`.
3. NPC definitions in game data (like items): id, name, dialogue entry node (for the dialogue plan), default level placement. Scene instances reference definitions by `NpcId`.
4. Face-the-player on interact; simple idle animation via the existing animation library.

**Done when:** walking up to the NPC in the Intro level shows a prompt and pressing E triggers a placeholder interaction (a `GD.Print` or stub dialogue box).

## Phase 2 — Placement and level integration

1. NPC spawn markers in level scenes (a `NpcSpawn` node with an `NpcId`), instanced by the level on ready — mirrors how `Spawn` works for the player (fix the inverted occupancy handlers in `Spawn.cs` while in there; see current-progress known issues).
2. `NpcRegistry` per loaded level: lookup of live NPC nodes by id, so dialogue/quest systems can address "the NPC named X".
3. Populate the Intro level with 2–3 NPCs.

**Done when:** NPCs appear at authored positions on level load and are queryable by id.

## Phase 3 — Behavior

1. **Behavior modes** per NPC (exported enum): `Stationary`, `Wander` (random point within a radius via `NavigationAgent3D`), `Patrol` (waypoint loop). Requires a navmesh bake in levels — coordinate with the party plan's follower work, which needs the same.
2. Interaction pauses behavior (stop, face player) and resumes after.
3. Simple schedule hook (optional): time-of-day posts, only if/when a day cycle exists — design the mode enum so it can grow.

**Done when:** a wandering NPC and a patrolling NPC coexist in the Intro level and still interact cleanly.

## Phase 4 — Persistence and state

1. **World-state bucket** in `GameState` (save plan Phase 5): per-NPC persisted state — `NpcId`, alive/removed flag, position (for wanderers), and a small `Dictionary<string,string>` flag store for dialogue/quest flags ("met_player", "gave_keycard").
2. Capture on save, restore on level load: spawn markers consult saved state (skip removed NPCs, restore positions/flags).
3. Bump `SaveVersion` with a migration.

**Done when:** an NPC flag set in dialogue survives save/quit/load.

## Phase 5 — Hooks for other systems

- **Dialogue:** `Interact` starts the NPC's Yarn node (dialogue plan Phase 2); NPC flag store backs Yarn variables (dialogue plan Phase 3).
- **Quests:** quest givers are just NPCs whose dialogue runs quest commands (quest plan Phase 3).
- **Recruitment:** party plan's recruit flow triggers from NPC dialogue.
- **Combat:** hostile NPCs / encounter triggers deferred to the combat design — keep `Npc.cs` friendly-only for now rather than speculatively generalizing.
- **Roles:** the one-subclass-per-role shape this plan produced (quest giver, recruit, shopkeeper, challenger) is being replaced by composable role resources on the definition — see [npc-composition.md](npc-composition.md).

## Decisions to settle early

| Decision | Recommendation |
|----------|----------------|
| NPCs as `CharacterEntity`? | No — keep `CharacterEntity` for party/combat-capable characters. NPCs get their own lighter definition + flag store; promote an NPC to `CharacterEntity` only on recruitment. |
| Interaction targeting | Nearest-in-range via Area3D overlap (no raycast aiming) — right feel for a 3rd-person RPG and much simpler. |
| Navmesh timing | Bake navigation into the Intro level during Phase 3 and share it with party followers. |
