using Godot;
using System;
using System.Collections.Generic;

// The Inventory tab of the in-game menu: category tabs across the top filter
// the party's shared stacks; selecting a stack shows its details plus the
// actions that apply to it (use / equip / drop), targeted at the party member
// picked in the dropdown.
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

	[Export]
	public Label EquippedLabel;

	[Export]
	public OptionButton MemberSelect;

	[Export]
	public Button UseButton;

	[Export]
	public Button EquipButton;

	[Export]
	public Button DropButton;

	[Export]
	public Label ActionResultLabel;

	// Index-aligned with the TabBar's tabs; null means no filter.
	private static readonly ItemCategory?[] TabFilters =
	{
		null,
		ItemCategory.Weapon,
		ItemCategory.Armor,
		ItemCategory.Consumable,
		ItemCategory.QuestItem,
	};

	private static readonly (EQUIPSLOT Slot, string Name)[] SlotNames =
	{
		(EQUIPSLOT.HEAD, "Head"),
		(EQUIPSLOT.EYES, "Eyes"),
		(EQUIPSLOT.LEFT_HAND, "Left Hand"),
		(EQUIPSLOT.RIGHT_HAND, "Right Hand"),
		(EQUIPSLOT.CHEST, "Chest"),
		(EQUIPSLOT.LEGS, "Legs"),
	};

	// Item ids backing the currently listed rows, in display order.
	private readonly List<uint> listedItemIds = new();

	// Item id of the selected row; survives Refresh so an action on a stack
	// (use one medkit, say) doesn't force reselecting it.
	private uint? selectedItemId;

	public override void _Ready()
	{
		CategoryTabs.TabChanged += _ => Refresh();
		StackList.ItemSelected += OnStackSelected;
		MemberSelect.ItemSelected += _ => UpdateDetails();
		UseButton.Pressed += OnUsePressed;
		EquipButton.Pressed += OnEquipPressed;
		DropButton.Pressed += OnDropPressed;
		VisibilityChanged += () =>
		{
			if (IsVisibleInTree())
			{
				ActionResultLabel.Text = "";
				Refresh();
			}
		};
	}

	public void Refresh()
	{
		RefreshMembers();
		listedItemIds.Clear();
		StackList.Clear();
		var inventory = SaveManager.Instance?.CurrentState?.Inventory;
		if (inventory != null)
		{
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
		var keptRow = selectedItemId == null ? -1 : listedItemIds.IndexOf(selectedItemId.Value);
		if (keptRow >= 0)
		{
			StackList.Select(keptRow);
		}
		else
		{
			selectedItemId = null;
		}
		UpdateDetails();
	}

	// Rebuilds the party dropdown (names go stale as HP changes), keeping the
	// same member selected where possible.
	private void RefreshMembers()
	{
		var previousMemberId = SelectedMember()?.Id;
		MemberSelect.Clear();
		var party = SaveManager.Instance?.CurrentState?.Party;
		if (party == null)
		{
			return;
		}
		for (var i = 0; i < party.Count; i++)
		{
			MemberSelect.AddItem($"{party[i].Name} ({party[i].HealthPoints}/{party[i].MaxHealthPoints} HP)");
			if (party[i].Id == previousMemberId)
			{
				MemberSelect.Select(i);
			}
		}
		if (MemberSelect.Selected < 0 && MemberSelect.ItemCount > 0)
		{
			MemberSelect.Select(0);
		}
	}

	private void OnStackSelected(long index)
	{
		selectedItemId = listedItemIds[(int)index];
		ActionResultLabel.Text = "";
		UpdateDetails();
	}

	private void UpdateDetails()
	{
		var item = SelectedItem();
		var member = SelectedMember();
		ItemNameLabel.Text = item?.Name ?? "";
		ItemDescriptionLabel.Text = DescriptionText(item);
		EquippedLabel.Text = EquippedText(member);
		UseButton.Disabled = item is not ConsumableItem || member == null;
		EquipButton.Disabled = item is not EquippableItem || member == null;
		DropButton.Disabled = item == null || item.Category == ItemCategory.QuestItem;
	}

	private void OnUsePressed()
	{
		var state = SaveManager.Instance?.CurrentState;
		var member = SelectedMember();
		if (state == null || member == null || SelectedItem() is not ConsumableItem consumable)
		{
			return;
		}
		if (member.HealthPoints >= member.MaxHealthPoints)
		{
			ActionResultLabel.Text = $"{member.Name} is already at full health.";
			return;
		}
		if (!state.Inventory.Remove(consumable.Id))
		{
			return;
		}
		member.HealthPoints = Math.Min(member.MaxHealthPoints, member.HealthPoints + consumable.HealAmount);
		Refresh();
		ActionResultLabel.Text = $"{member.Name} used the {consumable.Name}: now {member.HealthPoints}/{member.MaxHealthPoints} HP.";
	}

	private void OnEquipPressed()
	{
		var state = SaveManager.Instance?.CurrentState;
		var member = SelectedMember();
		if (state == null || member == null || SelectedItem() is not EquippableItem equippable)
		{
			return;
		}
		if (!state.Inventory.Remove(equippable.Id))
		{
			return;
		}
		member.EquipSlots ??= new CharacterEquipSlots();
		var replacedId = member.EquipSlots.Equip(equippable);
		var message = $"{member.Name} equipped the {equippable.Name}.";
		if (replacedId != null)
		{
			state.Inventory.Add(replacedId.Value);
			message += $" The {ItemCatalog.Get(replacedId.Value)?.Name} went back to the inventory.";
		}
		Refresh();
		ActionResultLabel.Text = message;
	}

	private void OnDropPressed()
	{
		var state = SaveManager.Instance?.CurrentState;
		var item = SelectedItem();
		if (state == null || item == null || item.Category == ItemCategory.QuestItem)
		{
			return;
		}
		if (!state.Inventory.Remove(item.Id))
		{
			return;
		}
		Refresh();
		ActionResultLabel.Text = $"Discarded one {item.Name}.";
	}

	private Item SelectedItem() =>
		selectedItemId == null ? null : ItemCatalog.Get(selectedItemId.Value);

	private CharacterEntity SelectedMember()
	{
		var party = SaveManager.Instance?.CurrentState?.Party;
		var index = MemberSelect.Selected;
		return party != null && index >= 0 && index < party.Count ? party[index] : null;
	}

	private static string DescriptionText(Item item) => item switch
	{
		null => "",
		Weapon weapon => $"{weapon.Description}\n\nDamage: {weapon.PhysicalDamage}",
		Armor armor => $"{armor.Description}\n\nDefense: {armor.PhysicalDefense}",
		ConsumableItem consumable => $"{consumable.Description}\n\nRestores {consumable.HealAmount} HP.",
		_ => item.Description,
	};

	private static string EquippedText(CharacterEntity member)
	{
		if (member == null)
		{
			return "";
		}
		var lines = new List<string>();
		foreach (var (slot, name) in SlotNames)
		{
			var itemId = member.EquipSlots?.GetItemId(slot);
			if (itemId != null)
			{
				lines.Add($"  {name}: {ItemCatalog.Get(itemId.Value)?.Name ?? "(unknown item)"}");
			}
		}
		return lines.Count == 0
			? $"{member.Name} has nothing equipped."
			: $"{member.Name} — equipped:\n{string.Join("\n", lines)}";
	}
}
