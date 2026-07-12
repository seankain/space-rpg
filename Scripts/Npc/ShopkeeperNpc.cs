using Godot;
using System.Collections.Generic;

// A merchant NPC: talking to them offers to open the shop trading screen.
// The shop's opening stock and bankroll are declared in the scene through
// the exports below; the Merchant built from them lives only as long as the
// NPC node, so stock resets when the interior reloads (persisting merchant
// state into saves is future work).
public partial class ShopkeeperNpc : Npc
{
	// The trading screen this shopkeeper drives, declared alongside the NPC
	// in the interior scene (e.g. ShopInterior.tscn's ShopUi/ShopMenu).
	[Export]
	public ShopMenu ShopMenu;

	[Export]
	public uint StartingCredits = 400;

	// Parallel arrays declaring the opening stock: StockItemIds[i] of
	// StockQuantities[i] each (quantity defaults to 1 when the arrays are
	// uneven). Parallel because Godot can't export a Dictionary<uint, uint>.
	[Export]
	public int[] StockItemIds = System.Array.Empty<int>();

	[Export]
	public int[] StockQuantities = System.Array.Empty<int>();

	public Merchant Merchant { get; private set; }

	public override void _Ready()
	{
		base._Ready();
		Merchant = new Merchant { Name = DisplayName, Credits = StartingCredits };
		for (var i = 0; i < StockItemIds.Length; i++)
		{
			var itemId = (uint)StockItemIds[i];
			if (ItemCatalog.Get(itemId) == null)
			{
				GD.PushWarning($"Shopkeeper '{DisplayName}' stock lists unknown item id {itemId}.");
				continue;
			}
			Merchant.Stock.Add(itemId, i < StockQuantities.Length ? (uint)StockQuantities[i] : 1);
		}
	}

	protected override void OnInteract()
	{
		if (ShopMenu == null)
		{
			GD.PushWarning($"Shopkeeper '{DisplayName}' has no ShopMenu assigned.");
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
					Action = () => Callable.From(() => ShopMenu.Open(Merchant, ShowPromptIfPlayerInRange)).CallDeferred(),
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
