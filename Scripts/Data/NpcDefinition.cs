using Godot;
using System.Collections.Generic;

// How an NPC moves when nobody is engaging it (npc-system plan Phase 3's
// modes, attached as behavior child nodes per the npc-composition plan).
public enum NpcBehaviorMode
{
	Stationary,
	Wander,
	Patrol,
}

// One NPC, fully described by data (docs/plans/npc-resource-files.md): who
// they are, where they spawn, and what they start with. Authored as one
// .tres per NPC under Resources/Npcs and loaded by NpcDatabase. Definitions
// come from Godot's resource cache and are shared — treat them as immutable
// at runtime; anything mutable (a Merchant, a recruited CharacterEntity) is
// copied out, never written back.
//
// .tres files reference this script by path, so it must not move.
[GlobalClass]
public partial class NpcDefinition : Resource, INpcDefinition
{
	// Stable unique id, e.g. "intro.vex". The handle quests, saves, and
	// encounters use — permanent once shipped, like item/quest ids.
	// NpcDatabase enforces uniqueness at load.
	[Export]
	public string NpcId { get; set; } = "";

	[Export]
	public string DisplayName { get; set; } = "";

	// Where this NPC exists: the hosting scene (a chunked level like
	// Intro.tscn, or an interior like ShopInterior.tscn) ...
	[Export(PropertyHint.File, "*.tscn")]
	public string SpawnScenePath { get; set; } = "";

	// ... and, when that scene is chunked, which chunk. Ignored for interiors.
	[Export]
	public Vector2I ChunkCoords { get; set; }

	// Position within the chunk (chunk-local, [-32, 32)) or within the
	// interior scene. Y rotation in degrees so NPCs face the right way.
	[Export]
	public Vector3 LocalPosition { get; set; }

	[Export]
	public float RotationDegreesY { get; set; }

	// Composable interaction roles (docs/plans/npc-composition.md): each
	// contributes a conversation plus world actions, and Npc merges them at
	// interact time. Order sets the multi-role choice-menu order. Every NPC
	// instantiates the same Scenes/Npc.tscn.
	[Export]
	public NpcRole[] Roles { get; set; } = System.Array.Empty<NpcRole>();

	// Ambient movement (npc-composition plan Phase 4): Npc attaches the
	// matching behavior child node — behaviors are nodes with per-frame
	// work, roles stay interaction-only.
	[Export]
	public NpcBehaviorMode Behavior { get; set; } = NpcBehaviorMode.Stationary;

	// Wander only: how far from the spawn point the NPC roams.
	[Export]
	public float WanderRadius { get; set; } = 4f;

	// Patrol only: waypoints in the same local space as LocalPosition
	// (chunk-local / interior-local), walked as a loop.
	[Export]
	public Vector3[] PatrolPoints { get; set; } = System.Array.Empty<Vector3>();

	// Rig wrapper scene (Scenes/Characters/Rigs/*.tscn): the character model
	// with its AnimationPlayer pre-wired, root script CharacterRig. Null
	// keeps the placeholder capsule, tinted by BodyColor.
	[Export]
	public PackedScene Rig { get; set; }

	[Export]
	public Color BodyColor { get; set; } = Colors.White;

	// Character sheet: vitals and the six stats a runtime copy of this NPC
	// starts with (a recruited party member, a battle combatant without an
	// authored EnemyCatalog encounter). Null keeps the old shared template.
	[Export]
	public NpcStatBlock Stats { get; set; }

	// Starting wallet and inventory: shop bankroll/stock for shopkeepers,
	// carried items for future recruit/loot use.
	[Export]
	public uint Credits { get; set; }

	[Export]
	public NpcItemStack[] InitialItems { get; set; } = System.Array.Empty<NpcItemStack>();

	// Engine-free view for NpcIndex (validation and chunk lookups).
	int INpcDefinition.ChunkX => ChunkCoords.X;
	int INpcDefinition.ChunkZ => ChunkCoords.Y;
	IEnumerable<uint> INpcDefinition.ItemIds
	{
		get
		{
			foreach (var stack in InitialItems ?? System.Array.Empty<NpcItemStack>())
			{
				if (stack != null)
				{
					yield return stack.ItemId;
				}
			}
		}
	}
}
