using Godot;
using System.Collections.Generic;

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

	// Role variant to instantiate: RecruitNpc.tscn, ShopkeeperNpc.tscn, ...
	// (thin inherited scenes of Npc.tscn under Scenes/Npc carrying the role
	// script).
	[Export]
	public PackedScene NpcScene { get; set; }

	// Rigged character scene (KayKit gltf). Null keeps the placeholder
	// capsule, tinted by BodyColor.
	[Export]
	public PackedScene CharacterMesh { get; set; }

	[Export]
	public Color BodyColor { get; set; } = Colors.White;

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
