using System.Collections.Generic;

// The world-flag console verbs (npc-dialogue-yarn.md Phase 3), thin wrappers
// that hand the running GameState to the engine-free EditorFlagCommands.
// Godot-facing (they read SaveManager), so they live here rather than in the
// engine-free Commands/ folder — the same split as the quest and item verbs.
//
// Flags a conversation set are only visible here; nothing in the in-game menu
// shows them.

public sealed class FlagCommand : IConsoleCommand
{
    public string Name => "flag";
    public string Usage => "flag <set|clear|get> <name> [value]";
    public string Summary => "Set, clear, or read a world flag (dialogue state).";

    public CommandResult Execute(IReadOnlyList<string> args) =>
        EditorFlagCommands.Run(SaveManager.Instance?.CurrentState, args);
}

public sealed class FlagsCommand : IConsoleCommand
{
    public string Name => "flags";
    public string Usage => "flags";
    public string Summary => "List the world flags currently set.";

    public CommandResult Execute(IReadOnlyList<string> args) =>
        EditorFlagCommands.List(SaveManager.Instance?.CurrentState);
}
