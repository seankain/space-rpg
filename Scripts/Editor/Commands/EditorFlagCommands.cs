using System.Collections.Generic;
using System.Linq;
using System.Text;

// Engine-free logic behind the world-flag console verbs (npc-dialogue-yarn.md
// Phase 3). Flags are the one piece of save state with no UI anywhere — no
// quest log, no inventory tab — so without these an author can only reach a
// flag-gated branch by replaying the conversation that sets it. Mutates the
// running GameState that the save system persists, exactly like the quest and
// item commands, and engine-free for the same reason: it is unit-tested against
// a fabricated GameState.
public static class EditorFlagCommands
{
    // flag <set|clear|get> <name> [value]
    public static CommandResult Run(GameState state, IReadOnlyList<string> args)
    {
        if (state == null)
        {
            return NoGame();
        }
        if (args.Count < 1)
        {
            return Usage();
        }
        return args[0].ToLowerInvariant() switch
        {
            "set" => Set(state, args),
            "clear" => Clear(state, args),
            "get" => Get(state, args),
            _ => CommandResult.Fail($"Unknown flag subcommand '{args[0]}'. {UsageText}"),
        };
    }

    // flags: every flag currently set, with its value.
    public static CommandResult List(GameState state)
    {
        if (state == null)
        {
            return NoGame();
        }
        if (state.Flags.Count == 0)
        {
            return CommandResult.Ok("No world flags are set.");
        }
        var builder = new StringBuilder($"Flags ({state.Flags.Count}):");
        foreach (var flag in state.Flags.OrderBy(f => f.Key, System.StringComparer.Ordinal))
        {
            builder.Append('\n').Append("  ").Append(flag.Key).Append(" = ").Append(flag.Value);
        }
        return CommandResult.Ok(builder.ToString());
    }

    private static CommandResult Set(GameState state, IReadOnlyList<string> args)
    {
        if (!TryName(args, 1, out var name, out var error))
        {
            return CommandResult.Fail(error);
        }
        // Same default as the set_flag dialogue effect, so poking a flag from
        // the console matches what a conversation would have written.
        var value = args.Count > 2 ? args[2] : "true";
        if (string.IsNullOrEmpty(value))
        {
            return CommandResult.Fail("Use 'flag clear <name>' to unset a flag.");
        }
        state.SetFlag(name, value);
        return CommandResult.Ok($"Flag '{name}' = {value}.");
    }

    private static CommandResult Clear(GameState state, IReadOnlyList<string> args)
    {
        if (!TryName(args, 1, out var name, out var error))
        {
            return CommandResult.Fail(error);
        }
        var had = state.GetFlag(name);
        state.ClearFlag(name);
        return CommandResult.Ok(had == null
            ? $"Flag '{name}' was not set."
            : $"Cleared flag '{name}' (was {had}).");
    }

    private static CommandResult Get(GameState state, IReadOnlyList<string> args)
    {
        if (!TryName(args, 1, out var name, out var error))
        {
            return CommandResult.Fail(error);
        }
        var value = state.GetFlag(name);
        return CommandResult.Ok(value == null
            ? $"Flag '{name}' is not set."
            : $"Flag '{name}' = {value}  (reads as {(GameState.IsFlagTruthy(value) ? "set" : "not set")})");
    }

    private static bool TryName(IReadOnlyList<string> args, int index, out string name, out string error)
    {
        name = args.Count > index ? args[index] : null;
        error = null;
        if (string.IsNullOrWhiteSpace(name))
        {
            error = UsageText;
            return false;
        }
        // The dialogue vocabulary can't reference a flag whose name contains the
        // token separator, so the console won't create one either.
        error = TokenRef.ArgFormatError("flag", "flag name", name);
        return error == null;
    }

    private const string UsageText = "Usage: flag <set|clear|get> <name> [value]";

    private static CommandResult Usage() => CommandResult.Fail(UsageText);

    private static CommandResult NoGame() =>
        CommandResult.Fail("No game in progress. Start or load a game first.");
}
