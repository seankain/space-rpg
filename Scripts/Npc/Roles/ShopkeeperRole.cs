using Godot;

// The merchant role: talking offers to open the shop trading screen. The
// conversation lives in Resources/Dialogue/intro.shopkeeper.dialogue.json
// (dialogue-editor plan Phase 2); its "Let's trade" choice runs the open_shop
// effect, which finds this NPC's Merchant and the ShopMenu (NpcDialogueHost).
// This class keeps the per-NPC Merchant runtime state and the menu label.
//
// The shop's opening stock and bankroll come from the NpcDefinition (Credits +
// InitialItems); the Merchant built from them is per-NPC runtime state, living
// only as long as the NPC node, so stock resets when the interior reloads
// (persisting merchant state into saves is future work).
//
// .tres files reference this script by path, so it must not move.
[GlobalClass]
public partial class ShopkeeperRole : NpcRole
{
	public override string MenuLabel => "Let's trade";

	// The Merchant is mutable, so it belongs to the Npc node, not this
	// shared resource.
	public override object CreateRuntimeState(Npc npc)
	{
		var merchant = new Merchant
		{
			Name = npc.DisplayName,
			Credits = npc.Definition?.Credits ?? 0,
		};
		foreach (var stack in npc.Definition?.InitialItems ?? System.Array.Empty<NpcItemStack>())
		{
			if (stack == null)
			{
				continue;
			}
			if (ItemCatalog.Get(stack.ItemId) == null)
			{
				// NpcDatabase already rejects definitions with unknown item
				// ids; this guards the definition-less fallback path.
				GD.PushWarning($"Shopkeeper '{npc.DisplayName}' stock lists unknown item id {stack.ItemId}.");
				continue;
			}
			merchant.Stock.Add(stack.ItemId, stack.Quantity == 0 ? 1 : stack.Quantity);
		}
		return merchant;
	}
}
