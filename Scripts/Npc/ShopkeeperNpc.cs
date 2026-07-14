using Godot;
using System.Collections.Generic;

// A merchant NPC: talking to them offers to open the shop trading screen.
// The shop's opening stock and bankroll come from the NpcDefinition this NPC
// was spawned from (Credits + InitialItems); the Merchant built from them
// lives only as long as the NPC node, so stock resets when the interior
// reloads (persisting merchant state into saves is future work).
public partial class ShopkeeperNpc : Npc
{
	public Merchant Merchant { get; private set; }

	public override void _Ready()
	{
		base._Ready();
		Merchant = new Merchant { Name = DisplayName, Credits = Definition?.Credits ?? 0 };
		foreach (var stack in Definition?.InitialItems ?? System.Array.Empty<NpcItemStack>())
		{
			if (stack == null)
			{
				continue;
			}
			if (ItemCatalog.Get(stack.ItemId) == null)
			{
				// NpcDatabase already rejects definitions with unknown item
				// ids; this guards the definition-less fallback path.
				GD.PushWarning($"Shopkeeper '{DisplayName}' stock lists unknown item id {stack.ItemId}.");
				continue;
			}
			Merchant.Stock.Add(stack.ItemId, stack.Quantity == 0 ? 1 : stack.Quantity);
		}
	}

	protected override void OnInteract()
	{
		// Resolved at interact time: a data-spawned NPC can't carry a
		// NodePath export, and the menu (the hosting scene's UI layer) is
		// guaranteed ready long before anyone can talk to the keeper.
		if (GetTree().GetFirstNodeInGroup(ShopMenu.GroupName) is not ShopMenu shopMenu)
		{
			GD.PushWarning($"Shopkeeper '{DisplayName}' found no ShopMenu in the scene.");
			base.OnInteract();
			return;
		}
		DialogueManager.Instance.Start(new DialogueLine
		{
			Speaker = DisplayName,
			Text = "Welcome in. Best prices on the station — care to see my stock?",
			Choices = new List<DialogueChoice>
			{
				new DialogueChoice
				{
					Label = "Let's trade",
					// DialogueManager.End() recaptures the mouse after this
					// action runs, so open the menu a frame later to win
					// that race.
					Action = () => Callable.From(() => shopMenu.Open(Merchant, ShowPromptIfPlayerInRange)).CallDeferred(),
				},
				new DialogueChoice
				{
					Label = "Just looking",
					Next = new DialogueLine
					{
						Speaker = DisplayName,
						Text = "Take your time. Mind the merchandise.",
					},
				},
			},
		}, ShowPromptIfPlayerInRange);
	}
}
