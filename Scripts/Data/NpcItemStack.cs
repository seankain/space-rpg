using Godot;

// One stack in an NpcDefinition's starting inventory: a typed sub-resource
// that renders as an editable list in the inspector, replacing the parallel
// int[] exports ShopkeeperNpc used to need (Godot can't export a
// Dictionary<uint, uint>).
//
// .tres files reference this script by path, so it must not move.
[GlobalClass]
public partial class NpcItemStack : Resource
{
	// ItemCatalog id; NpcDatabase rejects definitions listing unknown ids.
	[Export]
	public uint ItemId { get; set; }

	[Export]
	public uint Quantity { get; set; } = 1;
}
