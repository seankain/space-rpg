using System.Collections.Generic;

// The item/credit console verbs (in-game-editor plan Phase 4), thin wrappers
// that hand the running GameState to the engine-free EditorItemCommands. Godot-
// facing (they read SaveManager), so they live here rather than in the
// engine-free Commands/ folder.
//
// Feedback is printed to the console: an already-open Inventory tab won't
// live-update, since InventoryMenu only refreshes when the tab becomes visible.

public sealed class GiveCommand : IConsoleCommand
{
    public string Name => "give";
    public string Usage => "give <itemId> [qty]";
    public string Summary => "Add an item to the party inventory by id.";

    public CommandResult Execute(IReadOnlyList<string> args) =>
        EditorItemCommands.Give(SaveManager.Instance?.CurrentState, args);
}

public sealed class GiveItemCommand : IConsoleCommand
{
    public string Name => "giveitem";
    public string Usage => "giveitem \"<name>\" [qty]";
    public string Summary => "Add an item to the party inventory by name.";

    public CommandResult Execute(IReadOnlyList<string> args) =>
        EditorItemCommands.GiveByName(SaveManager.Instance?.CurrentState, args);
}

public sealed class TakeItemCommand : IConsoleCommand
{
    public string Name => "takeitem";
    public string Usage => "takeitem <itemId> [qty]";
    public string Summary => "Remove an item from the party inventory.";

    public CommandResult Execute(IReadOnlyList<string> args) =>
        EditorItemCommands.Take(SaveManager.Instance?.CurrentState, args);
}

public sealed class CreditsCommand : IConsoleCommand
{
    public string Name => "credits";
    public string Usage => "credits <amount> | +<amount> | -<amount>";
    public string Summary => "Set or adjust party credits.";

    public CommandResult Execute(IReadOnlyList<string> args) =>
        EditorItemCommands.Credits(SaveManager.Instance?.CurrentState, args);
}

public sealed class ItemsCommand : IConsoleCommand
{
    public string Name => "items";
    public string Usage => "items";
    public string Summary => "List the item catalog (id, name, category).";

    public CommandResult Execute(IReadOnlyList<string> args) => EditorItemCommands.ListItems();
}
