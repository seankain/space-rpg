using Godot;
using System;
using System.Collections.Generic;

// The shop trading screen (Scenes/Menu/ShopMenu.tscn): the Buy tab lists the
// merchant's stock, the Sell tab the party inventory; selecting a row shows
// its details and price, and the action button executes the Trade. All
// controls are declared in the scene and wired here through exports, like
// InventoryMenu. Opened by an NPC's ShopkeeperRole via Open().
public partial class ShopMenu : Control
{
	// The open shop menu, if any. Gameplay input (Player movement, Npc /
	// Pickup / Door interaction) treats an open shop like an active dialogue.
	private static ShopMenu current;
	public static bool IsShopOpen => current != null;

	// How ShopkeeperRole finds the hosting scene's menu at interact time —
	// NPCs spawned from NpcDefinitions carry no NodePath wiring.
	public const string GroupName = "ShopMenu";

	[Export]
	public Label ShopNameLabel;

	[Export]
	public Label PlayerCreditsLabel;

	[Export]
	public Label ShopCreditsLabel;

	[Export]
	public TabBar ModeTabs;

	[Export]
	public ItemList StackList;

	[Export]
	public Label ItemNameLabel;

	[Export]
	public Label ItemDescriptionLabel;

	[Export]
	public Label PriceLabel;

	[Export]
	public Button ActionButton;

	[Export]
	public Label ResultLabel;

	[Export]
	public Button CloseButton;

	private Merchant merchant;
	private Action onClosed;

	// Item ids backing the currently listed rows, in display order.
	private readonly List<uint> listedItemIds = new();

	// Item id of the selected row; survives Refresh so a transaction on a
	// stack doesn't force reselecting it.
	private uint? selectedItemId;

	private bool Buying => ModeTabs.CurrentTab == 0;

	public override void _Ready()
	{
		AddToGroup(GroupName);
		ModeTabs.TabChanged += _ =>
		{
			selectedItemId = null;
			ResultLabel.Text = "";
			Refresh();
		};
		StackList.ItemSelected += OnStackSelected;
		ActionButton.Pressed += OnActionPressed;
		CloseButton.Pressed += Close;
	}

	public void Open(Merchant merchant, Action onClosed = null)
	{
		this.merchant = merchant;
		this.onClosed = onClosed;
		current = this;
		ShopNameLabel.Text = merchant.Name;
		ModeTabs.CurrentTab = 0;
		selectedItemId = null;
		ResultLabel.Text = "";
		Visible = true;
		Input.MouseMode = Input.MouseModeEnum.Visible;
		Refresh();
	}

	public void Close()
	{
		Visible = false;
		current = null;
		merchant = null;
		Input.MouseMode = Input.MouseModeEnum.Captured;
		var closed = onClosed;
		onClosed = null;
		closed?.Invoke();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (current == this && @event.IsActionPressed("ui_cancel"))
		{
			GetViewport().SetInputAsHandled();
			Close();
		}
	}

	private void Refresh()
	{
		listedItemIds.Clear();
		StackList.Clear();
		var state = SaveManager.Instance?.CurrentState;
		if (state == null || merchant == null)
		{
			return;
		}
		var source = Buying ? merchant.Stock : state.Inventory;
		foreach (var stack in source.Stacks)
		{
			var item = ItemCatalog.Get(stack.ItemId);
			if (item == null)
			{
				continue;
			}
			listedItemIds.Add(stack.ItemId);
			StackList.AddItem(stack.Quantity > 1 ? $"{item.Name} x{stack.Quantity}" : item.Name);
		}
		var keptRow = selectedItemId == null ? -1 : listedItemIds.IndexOf(selectedItemId.Value);
		if (keptRow >= 0)
		{
			StackList.Select(keptRow);
		}
		else
		{
			selectedItemId = null;
		}
		PlayerCreditsLabel.Text = $"Your credits: {state.Credits}";
		ShopCreditsLabel.Text = $"{merchant.Name}: {merchant.Credits} cr";
		UpdateDetails();
	}

	private void OnStackSelected(long index)
	{
		selectedItemId = listedItemIds[(int)index];
		ResultLabel.Text = "";
		UpdateDetails();
	}

	private void UpdateDetails()
	{
		var item = selectedItemId == null ? null : ItemCatalog.Get(selectedItemId.Value);
		ItemNameLabel.Text = item?.Name ?? "";
		ItemDescriptionLabel.Text = item?.Description ?? "";
		ActionButton.Text = Buying ? "Buy" : "Sell";
		if (item == null)
		{
			PriceLabel.Text = "";
			ActionButton.Disabled = true;
		}
		else if (Buying)
		{
			PriceLabel.Text = $"Price: {Trade.BuyPrice(item)} cr";
			ActionButton.Disabled = false;
		}
		else if (Trade.IsTradable(item))
		{
			PriceLabel.Text = $"Sells for: {Trade.SellPrice(item)} cr";
			ActionButton.Disabled = false;
		}
		else
		{
			PriceLabel.Text = $"{merchant?.Name} won't buy this.";
			ActionButton.Disabled = true;
		}
	}

	private void OnActionPressed()
	{
		var state = SaveManager.Instance?.CurrentState;
		if (state == null || merchant == null || selectedItemId == null)
		{
			return;
		}
		string message;
		if (Buying)
		{
			Trade.TryBuy(state, merchant, selectedItemId.Value, out message);
		}
		else
		{
			Trade.TrySell(state, merchant, selectedItemId.Value, out message);
		}
		Refresh();
		ResultLabel.Text = message;
	}
}
