using System.Collections.Generic;

// The placement console verbs (in-game-editor plan Phase 3), thin wrappers over
// EditorPlacement. Godot-facing (they drive the running scene), so they live
// here rather than in the engine-free Commands/ folder while still satisfying
// the IConsoleCommand contract.

public sealed class SpawnCommand : IConsoleCommand
{
    public string Name => "spawn";
    public string Usage => "spawn <npcId> [here | <x> <y> <z>]";
    public string Summary => "Spawn a throwaway preview of an existing NPC (not saved).";

    public CommandResult Execute(IReadOnlyList<string> args) => EditorPlacement.SpawnExisting(args);
}

public sealed class PlaceCommand : IConsoleCommand
{
    public string Name => "place";
    public string Usage => "place <npcId> <displayName> [rig] [here | <x> <y> <z>]";
    public string Summary => "Author a new NPC at the cursor (or coordinates); savenpc to keep it.";

    public CommandResult Execute(IReadOnlyList<string> args) => EditorPlacement.Place(args);
}

public sealed class SaveNpcCommand : IConsoleCommand
{
    public string Name => "savenpc";
    public string Usage => "savenpc";
    public string Summary => "Write the placed NPC to Resources/Npcs/<Area>/<id>.tres.";

    public CommandResult Execute(IReadOnlyList<string> args) => EditorPlacement.Save();
}

public sealed class NudgeCommand : IConsoleCommand
{
    public string Name => "nudge";
    public string Usage => "nudge <dx> <dy> <dz>";
    public string Summary => "Move the placed NPC by an offset.";

    public CommandResult Execute(IReadOnlyList<string> args) => EditorPlacement.Nudge(args);
}

public sealed class RotateCommand : IConsoleCommand
{
    public string Name => "rotate";
    public string Usage => "rotate <degrees>";
    public string Summary => "Set the placed NPC's facing (Y rotation, degrees).";

    public CommandResult Execute(IReadOnlyList<string> args) => EditorPlacement.Rotate(args);
}

public sealed class CancelPlaceCommand : IConsoleCommand
{
    public string Name => "cancelplace";
    public string Usage => "cancelplace";
    public string Summary => "Discard the placed NPC preview without saving.";

    public CommandResult Execute(IReadOnlyList<string> args) => EditorPlacement.Cancel();
}

public sealed class ListCommand : IConsoleCommand
{
    public string Name => "list";
    public string Usage => "list <npcs [area]|windows>";
    public string Summary => "List known NPC definitions, or the open UI windows.";

    public CommandResult Execute(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return CommandResult.Fail("Usage: list npcs [area] | list windows");
        }
        if (string.Equals(args[0], "windows", System.StringComparison.OrdinalIgnoreCase))
        {
            return ListWindows();
        }
        if (!string.Equals(args[0], "npcs", System.StringComparison.OrdinalIgnoreCase))
        {
            return CommandResult.Fail("Usage: list npcs [area] | list windows");
        }
        var rest = new List<string>();
        for (var i = 1; i < args.Count; i++)
        {
            rest.Add(args[i]);
        }
        return EditorPlacement.ListNpcs(rest);
    }

    // The UI window stack, bottom to top, with the policy each entry carries —
    // for working out which window is holding the pointer or freezing play.
    private static CommandResult ListWindows()
    {
        var open = UiWindowManager.OpenWindows;
        if (open.Count == 0)
        {
            return CommandResult.Ok("No UI windows open — gameplay has the pointer.");
        }
        var lines = new List<string> { $"{open.Count} window(s), bottom to top:" };
        for (var i = 0; i < open.Count; i++)
        {
            var window = open[i];
            var flags = new List<string>();
            if (window.Exclusive) { flags.Add("exclusive"); }
            if (window.ReleasesPointer) { flags.Add("pointer"); }
            if (window.BlocksGameplay) { flags.Add("blocks-play"); }
            if (!window.ClosesOnCancel) { flags.Add("no-esc"); }
            var top = i == open.Count - 1 ? "  <- top" : "";
            lines.Add($"  {i}. {window.WindowName}   [{string.Join(", ", flags)}]{top}");
        }
        return CommandResult.Ok(string.Join("\n", lines));
    }
}
