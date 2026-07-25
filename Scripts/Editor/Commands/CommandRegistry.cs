using System;
using System.Collections.Generic;

// The set of registered console commands and the dispatch entry point
// (in-game-editor plan Phase 1). Names are matched case-insensitively.
// Engine-free so parsing and dispatch are unit-testable without the console UI.
public sealed class CommandRegistry
{
    private readonly Dictionary<string, IConsoleCommand> commands =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<IConsoleCommand> All => commands.Values;

    // Registers a command, replacing any existing one with the same name (last
    // registration wins, so a later phase can override a builtin if needed).
    public void Register(IConsoleCommand command)
    {
        if (command == null)
        {
            throw new ArgumentNullException(nameof(command));
        }
        commands[command.Name] = command;
    }

    public bool TryGet(string name, out IConsoleCommand command) =>
        commands.TryGetValue(name ?? string.Empty, out command);

    // Tokenizes a raw input line and runs the matching command. An empty line
    // is a no-op; an unrecognized name reports the error instead of throwing.
    public CommandResult Dispatch(string line)
    {
        var tokens = CommandTokenizer.Tokenize(line);
        if (tokens.Count == 0)
        {
            return CommandResult.Ok();
        }
        var name = tokens[0];
        if (!commands.TryGetValue(name, out var command))
        {
            return CommandResult.Fail($"Unknown command '{name}'. Type 'help' for a list.");
        }
        var args = tokens.GetRange(1, tokens.Count - 1);
        return command.Execute(args);
    }
}
