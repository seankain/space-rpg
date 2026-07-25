using System.Collections.Generic;

// One console verb (in-game-editor plan Phase 1). Commands are small objects
// registered in a CommandRegistry rather than a growing switch, so later
// phases (NPC placement, item/quest commands, the dialogue editor) add verbs
// without touching the console core. Engine-free by contract — a command that
// needs the running game reaches it through statics/autoloads at Execute time.
public interface IConsoleCommand
{
    // The word typed to invoke this command (matched case-insensitively).
    string Name { get; }

    // One-line usage string shown by `help`, e.g. "echo <text>".
    string Usage { get; }

    // Short description shown by `help`.
    string Summary { get; }

    // Runs the command with its already-tokenized arguments (the command name
    // removed). Never throws for bad input — returns CommandResult.Fail.
    CommandResult Execute(IReadOnlyList<string> args);
}
