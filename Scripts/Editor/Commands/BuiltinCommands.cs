using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

// The console's own commands (in-game-editor plan Phase 1). Engine-free, like
// the rest of the command layer; feature commands live in their own files.

// Prints its arguments back — a smoke test that the input/dispatch/output loop
// works, and handy in exec scripts.
public sealed class EchoCommand : IConsoleCommand
{
    public string Name => "echo";
    public string Usage => "echo <text>";
    public string Summary => "Print the given text back to the console.";

    public CommandResult Execute(IReadOnlyList<string> args) =>
        CommandResult.Ok(string.Join(" ", args));
}

// Wipes the scrollback via CommandResult.ClearOutput.
public sealed class ClearCommand : IConsoleCommand
{
    public string Name => "clear";
    public string Usage => "clear";
    public string Summary => "Clear the console output.";

    public CommandResult Execute(IReadOnlyList<string> args) => CommandResult.Clear();
}

// Lists every registered command with its usage and summary. Holds the
// registry it reports on so newly registered feature commands show up for free.
public sealed class HelpCommand : IConsoleCommand
{
    private readonly CommandRegistry registry;

    public HelpCommand(CommandRegistry registry)
    {
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public string Name => "help";
    public string Usage => "help";
    public string Summary => "List the available commands.";

    public CommandResult Execute(IReadOnlyList<string> args)
    {
        var builder = new StringBuilder("Available commands:");
        foreach (var command in registry.All.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append('\n').Append("  ").Append(command.Usage)
                .Append(" - ").Append(command.Summary);
        }
        return CommandResult.Ok(builder.ToString());
    }
}
