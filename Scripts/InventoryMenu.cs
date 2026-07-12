using Godot;
using System.Collections.Generic;

// The Inventory tab of the in-game menu: category tabs across the top
// filter the stack list; selecting a stack shows its details.
public partial class InventoryMenu : Control
{
	[Export]
	public TabBar CategoryTabs;

	[Export]
	public ItemList StackList;

	[Export]
	public Label ItemNameLabel;

	[Export]
	public Label ItemDescriptionLabel;

	// Index-aligned with the TabBar's tabs; null means no filter.
	private static readonly ItemCategory?[] TabFilters =
	{
		null,
		ItemCategory.Weapon,
		ItemCategory.Armor,
		ItemCategory.Consumable,
		ItemCategory.QuestItem,
	};

	// Item ids backing the currently listed rows, in display order.
	private readonly List<uint> listedItemIds = new();

	public override void _Ready()
	{
		CategoryTabs.TabChanged += _ => Refresh();
		StackList.ItemSelected += OnStackSelected;
		VisibilityChanged += () =>
		{
			if (IsVisibleInTree())
			{
				Refresh();
			}
		};
	}

	public void Refresh()
	{
		listedItemIds.Clear();
		StackList.Clear();
		ShowDetails(null);
		var inventory = SaveManager.Instance?.CurrentState?.Inventory;
		if (inventory == null)
		{
			return;
		}
		var filter = TabFilters[CategoryTabs.CurrentTab];
		foreach (var stack in inventory.Stacks)
		{
			var item = ItemCatalog.Get(stack.ItemId);
			if (item == null || (filter != null && item.Category != filter))
			{
				continue;
			}
			listedItemIds.Add(stack.ItemId);
			StackList.AddItem(stack.Quantity > 1 ? $"{item.Name} x{stack.Quantity}" : item.Name);
		}
	}

	private void OnStackSelected(long index)
	{
		ShowDetails(ItemCatalog.Get(listedItemIds[(int)index]));
	}

	private void ShowDetails(Item item)
	{
		ItemNameLabel.Text = item?.Name ?? "";
		ItemDescriptionLabel.Text = item?.Description ?? "";
	}
}
