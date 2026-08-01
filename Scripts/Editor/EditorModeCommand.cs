using System.Collections.Generic;

// The `editor` console verb (in-game-editor plan Phase 2): flips editor mode on
// or off. Godot-facing (it drives DevConsole), so it lives here rather than in
// the engine-free Commands/ folder; it still satisfies the engine-free
// IConsoleCommand contract. Registered under both "editor" and the "edit" alias.
public sealed class EditorModeCommand : IConsoleCommand
{
    private readonly DevConsole console;

    public EditorModeCommand(DevConsole console, string name = "editor")
    {
        this.console = console;
        Name = name;
    }

    public string Name { get; }
    public string Usage => $"{Name}";
    public string Summary => "Toggle editor mode (free camera + tool palette + gameplay lock).";

    public CommandResult Execute(IReadOnlyList<string> args)
    {
        var active = console.ToggleEditorMode();
        return CommandResult.Ok(active
            ? "Editor mode ON. WASD flies, Space/Ctrl rise and sink, Shift boosts, "
              + "hold right-mouse to look. Pick a tool on the right."
            : "Editor mode OFF.");
    }
}
