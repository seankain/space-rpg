using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

// Engine-free logic behind the item/credit console verbs (in-game-editor plan
// Phase 4). They mutate the running GameState the save system already persists,
// so a Save Game captures editor changes for free — no new save fields. Kept
// engine-free (GameState, Inventory, ItemCatalog are all engine-free) so the
// commands are unit-tested against a fabricated GameState; the Godot wrappers
// just supply the live SaveManager.Instance.CurrentState.
public static class EditorItemCommands
{
    // give <itemId> [qty]
    public static CommandResult Give(GameState state, IReadOnlyList<string> args)
    {
        if (state == null)
        {
            return NoGame();
        }
        if (args.Count < 1 || !uint.TryParse(args[0], out var itemId))
        {
            return CommandResult.Fail("Usage: give <itemId> [qty]");
        }
        return GiveResolved(state, ItemCatalog.Get(itemId), args, $"item id {itemId}");
    }

    // giveitem "<name>" [qty]
    public static CommandResult GiveByName(GameState state, IReadOnlyList<string> args)
    {
        if (state == null)
        {
            return NoGame();
        }
        if (args.Count < 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            return CommandResult.Fail("Usage: giveitem \"<name>\" [qty]");
        }
        var name = args[0];
        var matches = ItemCatalog.All
            .Where(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 0)
        {
            return CommandResult.Fail($"No item named '{name}'. Try 'items'.");
        }
        if (matches.Count > 1)
        {
            return CommandResult.Fail($"'{name}' is ambiguous ({matches.Count} matches). Use 'give <itemId>'.");
        }
        return GiveResolved(state, matches[0], args, $"'{name}'");
    }

    // takeitem <itemId> [qty]
    public static CommandResult Take(GameState state, IReadOnlyList<string> args)
    {
        if (state == null)
        {
            return NoGame();
        }
        if (args.Count < 1 || !uint.TryParse(args[0], out var itemId))
        {
            return CommandResult.Fail("Usage: takeitem <itemId> [qty]");
        }
        var item = ItemCatalog.Get(itemId);
        if (item == null)
        {
            return CommandResult.Fail($"Unknown item id {itemId}. Try 'items'.");
        }
        if (!TryQuantity(args, 1, out var qty, out var qtyError))
        {
            return CommandResult.Fail(qtyError);
        }
        if (!state.Inventory.Remove(itemId, qty))
        {
            return CommandResult.Fail($"Only holding {state.Inventory.CountOf(itemId)}x {item.Name}.");
        }
        return CommandResult.Ok($"Removed {qty}x {item.Name}. Inventory now holds {state.Inventory.CountOf(itemId)}.");
    }

    // credits <amount> (set) | +<amount> (add) | -<amount> (subtract, clamped at 0)
    public static CommandResult Credits(GameState state, IReadOnlyList<string> args)
    {
        if (state == null)
        {
            return NoGame();
        }
        if (args.Count < 1 || args[0].Length == 0)
        {
            return CommandResult.Fail("Usage: credits <amount> | +<amount> | -<amount>");
        }
        var token = args[0];
        var op = token[0];
        var numberPart = op is '+' or '-' ? token[1..] : token;
        if (!uint.TryParse(numberPart, out var amount))
        {
            return CommandResult.Fail("Amount must be a non-negative whole number.");
        }
        var before = state.Credits;
        state.Credits = op switch
        {
            '+' => (uint)Math.Min((ulong)before + amount, uint.MaxValue),
            '-' => amount >= before ? 0u : before - amount,
            _ => amount,
        };
        return CommandResult.Ok($"Credits {before} -> {state.Credits}.");
    }

    // items
    public static CommandResult ListItems()
    {
        var builder = new StringBuilder("Items:");
        foreach (var item in ItemCatalog.All.OrderBy(i => i.Id))
        {
            builder.Append('\n').Append("  ").Append(item.Id).Append("  ")
                .Append(item.Name).Append("  (").Append(item.Category).Append(')');
        }
        return CommandResult.Ok(builder.ToString());
    }

    private static CommandResult GiveResolved(GameState state, Item item, IReadOnlyList<string> args, string label)
    {
        if (item == null)
        {
            return CommandResult.Fail($"Unknown {label}. Try 'items'.");
        }
        if (!TryQuantity(args, 1, out var qty, out var qtyError))
        {
            return CommandResult.Fail(qtyError);
        }
        state.Inventory.Add(item.Id, qty);
        return CommandResult.Ok($"Gave {qty}x {item.Name}. Inventory now holds {state.Inventory.CountOf(item.Id)}.");
    }

    private static bool TryQuantity(IReadOnlyList<string> args, int index, out uint quantity, out string error)
    {
        error = null;
        quantity = 1;
        if (args.Count <= index)
        {
            return true; // omitted -> default of 1
        }
        if (!uint.TryParse(args[index], out quantity))
        {
            error = "Quantity must be a non-negative whole number.";
            return false;
        }
        if (quantity == 0)
        {
            error = "Quantity must be at least 1.";
            return false;
        }
        return true;
    }

    private static CommandResult NoGame() =>
        CommandResult.Fail("No game in progress. Start or load a game first.");
}
