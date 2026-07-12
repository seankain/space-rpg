using System;

// Pricing rules and execution for trades between the party and a Merchant.
// Engine-free like the rest of Scripts/Data; ShopMenu is a thin view over
// these calls. Every Try* returns a user-facing message either way, so the
// UI can show failures ("not enough credits") without duplicating the rules.
public static class Trade
{
    // Merchants sell at full catalog value and buy back at half, so a round
    // trip always costs the player credits.
    public static uint BuyPrice(Item item) => item.Value;
    public static uint SellPrice(Item item) => Math.Max(1, item.Value / 2);

    // Quest items and zero-value props can't be sold off.
    public static bool IsTradable(Item item) =>
        item != null && item.Value > 0 && item.Category != ItemCategory.QuestItem;

    public static bool TryBuy(GameState player, Merchant merchant, uint itemId, out string message)
    {
        var item = ItemCatalog.Get(itemId);
        if (item == null)
        {
            message = "That item no longer exists.";
            return false;
        }
        if (merchant.Stock.CountOf(itemId) == 0)
        {
            message = $"{merchant.Name} is out of {item.Name}.";
            return false;
        }
        var price = BuyPrice(item);
        if (player.Credits < price)
        {
            message = $"Not enough credits: the {item.Name} costs {price}, you have {player.Credits}.";
            return false;
        }
        merchant.Stock.Remove(itemId);
        player.Inventory.Add(itemId);
        player.Credits -= price;
        merchant.Credits += price;
        message = $"Bought the {item.Name} for {price} credits.";
        return true;
    }

    public static bool TrySell(GameState player, Merchant merchant, uint itemId, out string message)
    {
        var item = ItemCatalog.Get(itemId);
        if (!IsTradable(item))
        {
            message = item == null ? "That item no longer exists." : $"{merchant.Name} won't buy the {item.Name}.";
            return false;
        }
        var price = SellPrice(item);
        if (merchant.Credits < price)
        {
            message = $"{merchant.Name} can't afford the {item.Name} right now.";
            return false;
        }
        if (!player.Inventory.Remove(itemId))
        {
            message = $"You don't have a {item.Name} to sell.";
            return false;
        }
        merchant.Stock.Add(itemId);
        player.Credits += price;
        merchant.Credits -= price;
        message = $"Sold the {item.Name} for {price} credits.";
        return true;
    }
}
