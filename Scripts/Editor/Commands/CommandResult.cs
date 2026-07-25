// The outcome of running one console command (in-game-editor plan Phase 1).
// Engine-free so the command layer stays unit-testable; the DevConsole node
// turns a result into colored console output.
public sealed class CommandResult
{
    // Whether the command succeeded — the console prints Message in the
    // success or error color accordingly.
    public bool Success { get; }

    // Text to print. May be empty (a command that only has a side effect).
    public string Message { get; }

    // A request to wipe the scrollback before printing (the `clear` command).
    public bool ClearOutput { get; }

    private CommandResult(bool success, string message, bool clearOutput)
    {
        Success = success;
        Message = message ?? string.Empty;
        ClearOutput = clearOutput;
    }

    public static CommandResult Ok(string message = "") => new(true, message, false);

    public static CommandResult Fail(string message) => new(false, message, false);

    // Success with no message that tells the console to clear its output.
    public static CommandResult Clear() => new(true, string.Empty, true);
}
