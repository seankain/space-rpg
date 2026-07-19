# NPC Scene Composition — Implementation Plan

Goal: collapse the per-role NPC scene variants into a single `Npc.tscn`, move the mesh + `AnimationPlayer` into resource-specified **rig subscenes**, and replace the role subclass hierarchy with **composable role resources** — so one NPC can mix roles: a quest giver whose dialogue can branch into a battle, or a quest giver who becomes recruitable once their quest is complete.

## Where we are

- **Five scene variants that differ only by script.** `Scenes/Npc/{BattleNpc,BountyGiverNpc,QuestGiverNpc,RecruitNpc,ShopkeeperNpc}.tscn` are thin inherited scenes of `Scenes/Npc.tscn` whose entire content is a script override (plus one exported value on two of them). The scenes carry no structure of their own.
- **One role per NPC, enforced by inheritance.** Each role is a `Npc` subclass overriding `OnInteract` (its dialogue tree) and sometimes `_Ready` (a spawn-suppression check: `BattleNpc` stays gone when defeated, `RecruitNpc` when recruited). Mixing roles — quest giver who fights, quest giver who joins — means writing a new subclass per combination.
- **The mesh pipeline is runtime surgery, three times over.** `Npc.AddCharacterMesh`, `PartyMemberFollower.ApplyNpcCharacterMesh`, and `BattleScene`'s combatant setup each instantiate the raw KayKit `.glb`, apply the 180° forward flip, hunt for the `Skeleton3D`, and retarget an `AnimationPlayer`'s `RootNode` at it. Worse, the two animation libraries address the rig differently (`NpcAnimLib` tracks start at `Skeleton3D/…`, `player_animation_library` at `Rig_Medium/Skeleton3D/…`), so each site must pick a different root — exactly the kind of fragility a subscene that owns its own `AnimationPlayer` eliminates.
- **NPC data is already resource-driven** (npc-resource-files plan, shipped): one `NpcDefinition` `.tres` per NPC selects a role via `NpcScene` and a mesh via `CharacterMesh`; `NpcSpawner`/`ChunkManager` spawn from data. Definitions are cached and shared — immutable at runtime.
- Dialogue is C#-built `DialogueLine`/`DialogueChoice` trees; `DialogueChoice.Action` already takes arbitrary callbacks (that is how `BattleNpc` starts fights and `ShopkeeperNpc` opens the shop). The Yarn plan (npc-dialogue-yarn.md) intends to move dialogue *text and branching* to data eventually.

## Is composition tenable?

**Yes — and the codebase is most of the way there structurally.** The inherited scenes are already contentless; the spawner already instantiates from data; definitions are already resources. What inheritance actually encodes per role is: a dialogue tree, a spawn-suppression check, and a couple of config values. None of that needs a scene *or* a subclass — it needs data plus a small behavior contract. Specific findings:

- **All five roles are dialogue-shaped.** Each reduces to "contribute a conversation + perform actions on `GameState`". Since `DialogueChoice.Action` is an arbitrary callback, a battle branch inside a quest conversation is *already expressible* — the current design just provides no authoring seam for it because each role owns the whole `OnInteract`. Composition is that seam.
- **Godot supports the authoring model.** `[GlobalClass]` `Resource` subclasses export polymorphically: an `NpcRole[]` array on `NpcDefinition` renders as an inspector list where each element can be any concrete role, and serializes cleanly in `.tres` — the same mechanism `NpcItemStack[]` uses today.
- **The shared-resource constraint is real but solvable.** Definitions (and therefore role resources) come from Godot's resource cache and must stay immutable — yet `ShopkeeperNpc` owns a per-node `Merchant` and `RecruitNpc` a `joined` flag. So roles must be stateless templates; per-NPC runtime state is created by the role but owned by the `Npc` node (design below). This is a design rule to enforce, not a blocker.
- **Despawn semantics stop being obvious once roles mix.** Today "defeated → despawn" and "recruited → despawn" are role behaviors. A quest giver you can fight should probably *stay* after losing (their quest line still matters), while a recruited NPC always despawns (their follower replaces them). Policy has to move from the role to the definition — covered below.
- **Interplay with the Yarn plan is a shaping constraint, not a conflict.** Yarn will eventually own dialogue text and branching. Roles should therefore be built as **capability providers** — availability checks + world actions (`recruit`, `start battle`, `open shop`, quest transitions) + *interim* C# dialogue. When Yarn lands, its `<<recruit>>`/`<<start_quest>>` commands call the same role/manager APIs and the interim C# trees retire; the composition model survives intact. What would fight Yarn is baking multi-role *branching logic* deep into C# — keep the merge rules simple (below) and let Yarn take over interleaving later.
- **Cost is moderate and save-safe:** re-author five `.tres` files, delete five scenes and five subclass scripts, build ~4 rig wrapper scenes, standardize animation track addressing once. `NpcId`, `GameState.DefeatedNpcs`, and quest states are untouched — no `SaveVersion` bump.

## Design

### Rig subscenes — mesh + AnimationPlayer as one resource-specified unit

A wrapper scene per character model under `Scenes/Characters/Rigs/`:

```
Rogue.tscn
└─ RigRoot (Node3D, CharacterRig.cs — 180° forward flip baked into the transform)
   ├─ <KayKit Rogue_Hooded.glb instance>
   └─ AnimationPlayer (libraries assigned, RootNode wired at author time)
```

- `CharacterRig.cs`: a tiny script exposing the `AnimationPlayer` (exported reference) and a `Play(name)` convenience. Convention: visual forward is local **+Z** after the baked flip, matching `Npc.FacePlayer` and `PartyMemberFollower.TurnMeshToward`.
- `NpcDefinition.CharacterMesh` becomes `Rig` (a `PackedScene` of a wrapper). `Npc._Ready` instantiates it, drops the capsule, and calls `Play("Idle_Talking")` — the `RootNode` retargeting hack is deleted, along with the base scene's own `AnimationPlayer`.
- `PartyMemberFollower` and `BattleScene` consume the same wrappers, deleting the other two copies of the surgery. This also fixes their `FindByDisplayName` mesh lookups wholesale, since the rig arrives ready to play.
- **Track addressing (one-time content fix):** pick the rig scene root as the canonical `AnimationPlayer` root and make `NpcAnimLib`'s clips address `Rig_Medium/Skeleton3D/…` like `player_animation_library` already does. One library convention, wired once, in one place.
- The tinted-capsule fallback stays for rig-less definitions, so nothing blocks on art.

### Role resources

```csharp
[GlobalClass]
public abstract partial class NpcRole : Resource
{
    // Availability gate, usable by any role: e.g. a RecruitRole offered
    // only once a quest has succeeded. Empty = always available.
    [Export] public string RequiredQuestId { get; set; } = "";
    [Export] public QUESTSUCCESSSTATE RequiredQuestState { get; set; }

    // Veto spawning entirely (recruited members, despawn-on-defeat).
    public virtual bool ShouldSpawn(NpcDefinition def, GameState state) => true;

    public virtual bool IsAvailable(Npc npc, GameState state) => /* quest gate */;

    // Per-NPC mutable state (a shop's Merchant); owned by the Npc node,
    // never written back to this shared resource.
    public virtual object CreateRuntimeState(Npc npc) => null;

    // Choice label when this role shares the NPC with others.
    public abstract string MenuLabel { get; }
    public abstract DialogueLine BuildDialogue(Npc npc, GameState state);
}
```

Concrete roles port the existing subclasses 1:1 — `QuestGiverRole`, `BountyGiverRole` (`TargetNpcId` export), `ShopkeeperRole`, `RecruitRole` (`PartyCharacterId` export), `ChallengerRole` (`DespawnOnDefeat` export). `Npc` holds the spawned runtime state in a per-role slot and passes itself to `BuildDialogue`; roles read `GameState` through the same `SaveManager` path the subclasses use today.

`NpcDefinition` changes: **remove** `NpcScene`, **add** `[Export] NpcRole[] Roles`, rename `CharacterMesh` → `Rig`. `NpcSpawner`/`ChunkManager` always instantiate the single `Npc.tscn`; spawn suppression moves from `_Ready` overrides into a pre-instantiation check over `Roles` (cleaner than instantiate-then-`QueueFree`, and `NpcSpawner.Spawn` already runs before `_Ready`).

### Dialogue composition rules

- **No available roles** → the base "Hello there." wave-off.
- **One available role** → its dialogue plays directly. Every current NPC has exactly one role, so the intro station's conversations are preserved verbatim.
- **Two or more** → a greeting line with one choice per role (`MenuLabel`: "About that cube…", "Let's trade", "Can I join you?") plus "Never mind". Simple, predictable; richer interleaving is deliberately deferred to Yarn.
- **Cross-role flexibility comes from two mechanisms, not role-to-role coupling:**
  - *Gating*: `RequiredQuestId`/`RequiredQuestState` on any role — "recruitable after their quest succeeds" is pure data.
  - *Shared dialogue actions*: extract `BattleNpc`'s challenge pattern into a helper (e.g. `DialogueActions.StartBattle(npc, onWon)`) that **any** role's authored choices can use — a quest giver whose "hand it over" refusal starts a fight needs no `ChallengerRole` at all.

### Despawn and persistence policy

- **Recruited** → always despawn (`RecruitRole.ShouldSpawn` is false once the member is in the party; the follower replaces them). Unchanged.
- **Defeated** → always recorded in `GameState.DefeatedNpcs`, but despawn only when `ChallengerRole.DespawnOnDefeat` is true (the default, matching today's Vex). Multi-role NPCs set it false: they stay standing, the challenger role goes unavailable ("We settled that already"), and their other roles keep working.
- No save schema changes; `NpcId`, defeat flags, and quest states carry over untouched.

---

## Phase 1 — Rig subscenes

1. `CharacterRig.cs` + wrapper scenes for the characters in use (Rogue, Barbarian, Knight, Mage/others per the intro `.tres` files), with the track-addressing standardization.
2. `NpcDefinition.Rig`; `Npc`, `PartyMemberFollower`, and `BattleScene` switch to instantiating wrappers; delete all three retarget code paths.

**Done when:** every intro NPC, follower, and battle combatant renders through a rig wrapper, idle/battle animations play, and no runtime `RootNode` retargeting remains.

## Phase 2 — Role resources replace subclasses

1. `NpcRole` base + the five concrete roles, porting each subclass's dialogue and state logic.
2. `Npc.cs` composes dialogue per the merge rules; spawn suppression moves into the spawner's `ShouldSpawn` check.
3. Re-author the five `.tres` definitions with `Roles` arrays; delete `Scenes/Npc/*.tscn` and the five subclass scripts.
4. Update the `Tests/` coverage that touches definitions (`NpcIndexTests`) for the schema change.

**Done when:** the intro station plays identically to today — talk, quest, bounty, recruit, shop, battle; defeated/recruited NPCs stay gone across chunk reloads and saves — with one NPC scene and zero role subclasses.

## Phase 3 — Mixed-role content proof

1. Give one intro NPC two roles: quest giver + recruit, the recruit gated on the quest's `Success` (e.g. Chief Marlow joins once the Maguffin is returned).
2. Author a battle branch inside a quest conversation via the shared `StartBattle` action (a demand the player can refuse at swordpoint), with `DespawnOnDefeat` false so the quest line survives the fight.

**Done when:** the same NPC gives a quest and later joins the party; a quest dialogue choice starts a battle whose outcome persists, and the NPC's remaining roles still function afterward.

## Phase 4 — Behaviors stay nodes (future)

Wander/patrol from npc-system Phase 3 arrive as **child nodes**, not roles — the split to preserve: *roles are interaction verbs* (data-authorable resources, no per-frame work); *behaviors are continuous processing* (nodes with `_PhysicsProcess`). The single-scene model makes attaching them uniform.

## Decisions to settle early

| Decision | Recommendation |
|----------|----------------|
| Roles as resources vs child nodes | **Resources** — authorable inside the existing `.tres` files with zero scene assembly, and they compose in data. Reserve nodes for ticking behaviors (Phase 4). |
| Merge `BountyGiverRole` into `QuestGiverRole`? | Keep separate classes for now (their dialogue is bespoke C#); they collapse into one data-driven quest role naturally when Yarn takes over the text. |
| Where dialogue text lives | In role C# until the Yarn plan's Phase 2+ — then roles keep availability + actions and Yarn takes the words. Don't build a parallel text-in-resource system in the interim. |
| Multi-role presentation | Choice menu under a greeting. Interleaved/contextual weaving is a Yarn-era concern; don't encode it in C# merge logic. |
| Rename `CharacterMesh` → `Rig` now? | Yes — Phase 2 re-authors every `.tres` anyway, so the rename is free there. |
| Runtime state ownership | Role resources stay immutable templates; `CreateRuntimeState` output lives on the `Npc` node and dies with it (merchant stock persistence remains future work, unchanged). |
