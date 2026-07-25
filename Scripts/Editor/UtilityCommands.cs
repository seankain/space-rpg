using System.Collections.Generic;
using System.Globalization;
using Godot;

// Convenience console verbs (in-game-editor plan Phase 6): snapshot the game,
// teleport for QA, and run a script of commands. Godot-facing, so they live
// outside the engine-free Commands/ folder.

public sealed class SaveCommand : IConsoleCommand
{
    public string Name => "save";
    public string Usage => "save";
    public string Summary => "Write a new save slot capturing the current game.";

    public CommandResult Execute(IReadOnlyList<string> args)
    {
        var manager = SaveManager.Instance;
        if (manager == null || !manager.GameInProgress)
        {
            return CommandResult.Fail("No game in progress to save.");
        }
        var meta = manager.CreateSave();
        return CommandResult.Ok($"Saved slot #{meta.SaveNumber} ({meta.SaveLocationFriendlyName}).");
    }
}

public sealed class GotoCommand : IConsoleCommand
{
    private readonly DevConsole console;

    public GotoCommand(DevConsole console)
    {
        this.console = console;
    }

    public string Name => "goto";
    public string Usage => "goto <npcId> | goto <x> <y> <z>";
    public string Summary => "Teleport the player to an NPC or to coordinates.";

    public CommandResult Execute(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return CommandResult.Fail("Usage: goto <npcId> | goto <x> <y> <z>");
        }
        if (console.GetTree()?.GetFirstNodeInGroup(Player.GroupName) is not Node3D player)
        {
            return CommandResult.Fail("No player in the scene.");
        }
        Vector3 target;
        if (args.Count >= 3 && TryFloat(args[0], out var x) && TryFloat(args[1], out var y) && TryFloat(args[2], out var z))
        {
            target = new Vector3(x, y, z);
        }
        else
        {
            var definition = NpcDatabase.Get(args[0]);
            if (definition == null)
            {
                return CommandResult.Fail($"Expected coordinates or a known NPC id; '{args[0]}' is neither.");
            }
            // Chunk origin + local offset; interiors have ChunkCoords (0,0) so
            // this is just the local position.
            target = new Vector3(
                definition.ChunkCoords.X * ChunkManager.ChunkSize + definition.LocalPosition.X,
                definition.LocalPosition.Y,
                definition.ChunkCoords.Y * ChunkManager.ChunkSize + definition.LocalPosition.Z);
        }
        player.GlobalPosition = target;
        return CommandResult.Ok($"Teleported to ({target.X:0.0}, {target.Y:0.0}, {target.Z:0.0}).");
    }

    private static bool TryFloat(string token, out float value) =>
        float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}

public sealed class ExecCommand : IConsoleCommand
{
    private readonly DevConsole console;

    public ExecCommand(DevConsole console)
    {
        this.console = console;
    }

    public string Name => "exec";
    public string Usage => "exec <res://path/to/script.txt>";
    public string Summary => "Run console commands from a text file (# lines are comments).";

    public CommandResult Execute(IReadOnlyList<string> args)
    {
        if (args.Count < 1)
        {
            return CommandResult.Fail("Usage: exec <res://path/to/script.txt>");
        }
        var path = args[0];
        if (!FileAccess.FileExists(path))
        {
            return CommandResult.Fail($"No file at '{path}'.");
        }
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            return CommandResult.Fail($"Could not open '{path}': {FileAccess.GetOpenError()}.");
        }
        var count = console.ExecuteScript(file.GetAsText());
        return CommandResult.Ok($"Ran {count} command(s) from {path}.");
    }
}
