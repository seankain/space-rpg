using System;
using System.Collections.Generic;
using System.Linq;

public class ItemStack
{
    public uint ItemId {get;set;}
    public uint Quantity {get;set;}
}

// Party-shared inventory: unlimited stacks, per-item stack caps from the
// catalog. Engine-free like the rest of Scripts/Data so it serializes
// straight into GameState.
public class Inventory
{
    public List<ItemStack> Stacks {get;set;} = new();

    public uint CountOf(uint itemId) =>
        (uint)Stacks.Where(s => s.ItemId == itemId).Sum(s => s.Quantity);

    public void Add(uint itemId, uint quantity = 1)
    {
        if (quantity == 0)
        {
            return;
        }
        var definition = ItemCatalog.Get(itemId)
            ?? throw new ArgumentException($"Unknown item id {itemId}", nameof(itemId));
        var maxStack = Math.Max(1, definition.MaxStackSize);
        // Top up existing stacks before opening new ones.
        foreach (var stack in Stacks.Where(s => s.ItemId == itemId && s.Quantity < maxStack))
        {
            var space = maxStack - stack.Quantity;
            var moved = Math.Min(space, quantity);
            stack.Quantity += moved;
            quantity -= moved;
            if (quantity == 0)
            {
                return;
            }
        }
        while (quantity > 0)
        {
            var moved = Math.Min(maxStack, quantity);
            Stacks.Add(new ItemStack { ItemId = itemId, Quantity = moved });
            quantity -= moved;
        }
    }

    // Removes up to `quantity`; returns false (removing nothing) if the
    // inventory doesn't hold enough.
    public bool Remove(uint itemId, uint quantity = 1)
    {
        if (CountOf(itemId) < quantity)
        {
            return false;
        }
        for (var i = Stacks.Count - 1; i >= 0 && quantity > 0; i--)
        {
            if (Stacks[i].ItemId != itemId)
            {
                continue;
            }
            var moved = Math.Min(Stacks[i].Quantity, quantity);
            Stacks[i].Quantity -= moved;
            quantity -= moved;
            if (Stacks[i].Quantity == 0)
            {
                Stacks.RemoveAt(i);
            }
        }
        return true;
    }
}
