using Godot;
using System.Collections.Generic;

// The merchant conversation (ported from the old ShopkeeperNpc subclass):
// talking offers to open the shop trading screen. The shop's opening stock
// and bankroll come from the NpcDefinition (Credits + InitialItems); the
// Merchant built from them is per-NPC runtime state, living only as long as
// the NPC node, so stock resets when the interior reloads (persisting
// merchant state into saves is future work).
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

	public override DialogueLine BuildDialogue(Npc npc, GameState state)
	{
		// Resolved at interact time: a data-spawned NPC can't carry a
		// NodePath export, and the menu (the hosting scene's UI layer) is
		// guaranteed ready long before anyone can talk to the keeper.
		if (npc.GetTree().GetFirstNodeInGroup(ShopMenu.GroupName) is not ShopMenu shopMenu
			|| npc.GetRoleState(this) is not Merchant merchant)
		{
			GD.PushWarning($"Shopkeeper '{npc.DisplayName}' found no ShopMenu or Merchant.");
			return base.BuildDialogue(npc, state);
		}
		return new DialogueLine
		{
			Speaker = npc.DisplayName,
			Text = "Welcome in. Best prices on the station — care to see my stock?",
			Choices = new List<DialogueChoice>
			{
				new DialogueChoice
				{
					Label = "Let's trade",
					// DialogueManager.End() recaptures the mouse after this
					// action runs, so open the menu a frame later to win
					// that race.
					Action = () => Callable.From(() => shopMenu.Open(merchant, npc.ShowPromptIfPlayerInRange)).CallDeferred(),
				},
				new DialogueChoice
				{
					Label = "Just looking",
					Next = new DialogueLine
					{
						Speaker = npc.DisplayName,
						Text = "Take your time. Mind the merchandise.",
					},
				},
			},
		};
	}
}
