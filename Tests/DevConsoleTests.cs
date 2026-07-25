using System.Collections.Generic;
using Xunit;

// In-game-editor plan Phase 1 acceptance: the engine-free console layer — the
// tokenizer, registry dispatch, and the builtin echo/help/clear commands. The
// DevConsole node (UI, input, mouse mode) is exercised in the running editor.
public class DevConsoleTests
{
    // ---- Tokenizer ----

    [Fact]
    public void TokenizeSplitsOnWhitespace()
    {
        Assert.Equal(new[] { "give", "4", "2" }, CommandTokenizer.Tokenize("give 4 2"));
    }

    [Fact]
    public void TokenizeCollapsesExtraWhitespace()
    {
        Assert.Equal(new[] { "echo", "hi" }, CommandTokenizer.Tokenize("   echo    hi   "));
    }

    [Fact]
    public void TokenizeKeepsQuotedArgumentsWhole()
    {
        Assert.Equal(new[] { "give", "Vac-Suit Plating", "2" },
            CommandTokenizer.Tokenize("give \"Vac-Suit Plating\" 2"));
    }

    [Fact]
    public void TokenizeYieldsEmptyTokenForEmptyQuotes()
    {
        Assert.Equal(new[] { "say", "" }, CommandTokenizer.Tokenize("say \"\""));
    }

    [Fact]
    public void TokenizeEmptyLineYieldsNoTokens()
    {
        Assert.Empty(CommandTokenizer.Tokenize(""));
        Assert.Empty(CommandTokenizer.Tokenize("    "));
        Assert.Empty(CommandTokenizer.Tokenize(null));
    }

    // ---- Registry dispatch ----

    private static CommandRegistry NewRegistry()
    {
        var registry = new CommandRegistry();
        registry.Register(new HelpCommand(registry));
        registry.Register(new ClearCommand());
        registry.Register(new EchoCommand());
        return registry;
    }

    [Fact]
    public void DispatchRunsMatchingCommand()
    {
        var result = NewRegistry().Dispatch("echo hello there");
        Assert.True(result.Success);
        Assert.Equal("hello there", result.Message);
    }

    [Fact]
    public void DispatchIsCaseInsensitive()
    {
        var result = NewRegistry().Dispatch("ECHO hi");
        Assert.True(result.Success);
        Assert.Equal("hi", result.Message);
    }

    [Fact]
    public void DispatchUnknownCommandFails()
    {
        var result = NewRegistry().Dispatch("bogus 1 2 3");
        Assert.False(result.Success);
        Assert.Contains("bogus", result.Message);
    }

    [Fact]
    public void DispatchEmptyLineIsANoOp()
    {
        var result = NewRegistry().Dispatch("   ");
        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.Message);
    }

    [Fact]
    public void LatestRegistrationWins()
    {
        var registry = new CommandRegistry();
        registry.Register(new EchoCommand());
        registry.Register(new StubCommand("echo", "overridden"));
        Assert.Equal("overridden", registry.Dispatch("echo anything").Message);
    }

    // ---- Builtin commands ----

    [Fact]
    public void EchoJoinsArgumentsWithSpaces()
    {
        var result = new EchoCommand().Execute(new List<string> { "a", "b", "c" });
        Assert.True(result.Success);
        Assert.Equal("a b c", result.Message);
    }

    [Fact]
    public void ClearRequestsOutputWipe()
    {
        var result = new ClearCommand().Execute(new List<string>());
        Assert.True(result.Success);
        Assert.True(result.ClearOutput);
    }

    [Fact]
    public void HelpListsEveryRegisteredCommand()
    {
        var result = NewRegistry().Dispatch("help");
        Assert.True(result.Success);
        Assert.Contains("echo", result.Message);
        Assert.Contains("help", result.Message);
        Assert.Contains("clear", result.Message);
    }

    [Fact]
    public void HelpReflectsLaterRegisteredCommands()
    {
        var registry = NewRegistry();
        registry.Register(new StubCommand("teleport", "moved"));
        Assert.Contains("teleport", registry.Dispatch("help").Message);
    }

    private sealed class StubCommand : IConsoleCommand
    {
        private readonly string message;

        public StubCommand(string name, string message)
        {
            Name = name;
            this.message = message;
        }

        public string Name { get; }
        public string Usage => Name;
        public string Summary => "test stub";

        public CommandResult Execute(IReadOnlyList<string> args) => CommandResult.Ok(message);
    }
}
