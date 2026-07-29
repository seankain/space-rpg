using Xunit;

// The world-flag console verbs (npc-dialogue-yarn.md Phase 3). Flags have no UI
// anywhere, so these are the only way to see or fake dialogue state in a debug
// session — which makes them worth the same test treatment as the quest verbs.
public class EditorFlagCommandTests
{
    private static CommandResult Run(GameState state, params string[] args) =>
        EditorFlagCommands.Run(state, args);

    [Fact]
    public void SetDefaultsToTrueAndGetReadsItBack()
    {
        var state = new GameState();

        Assert.True(Run(state, "set", "met_hale").Success);
        Assert.Equal("true", state.GetFlag("met_hale"));
        Assert.Contains("= true", Run(state, "get", "met_hale").Message);
    }

    [Fact]
    public void SetTakesAValue()
    {
        var state = new GameState();
        Assert.True(Run(state, "set", "hale_mood", "angry").Success);
        Assert.Equal("angry", state.GetFlag("hale_mood"));
    }

    [Fact]
    public void GetSaysHowTheValueReadsToAConditionAndWhenItIsUnset()
    {
        var state = new GameState();
        Assert.Contains("not set", Run(state, "get", "met_hale").Message);

        // A flag holding "false" exists but reads as unset to flag(name), which
        // is confusing enough to spell out.
        state.SetFlag("met_hale", "false");
        var message = Run(state, "get", "met_hale").Message;
        Assert.Contains("= false", message);
        Assert.Contains("reads as not set", message);
    }

    [Fact]
    public void ClearRemovesTheFlagAndSaysWhatItWas()
    {
        var state = new GameState();
        state.SetFlag("met_hale", "true");

        Assert.Contains("was true", Run(state, "clear", "met_hale").Message);
        Assert.Empty(state.Flags);
        Assert.Contains("was not set", Run(state, "clear", "met_hale").Message);
    }

    [Fact]
    public void ListShowsEveryFlagSorted()
    {
        var state = new GameState();
        state.SetFlag("met_hale", "true");
        state.SetFlag("hale_mood", "angry");

        var message = EditorFlagCommands.List(state).Message;
        Assert.Contains("Flags (2)", message);
        Assert.True(
            message.IndexOf("hale_mood", System.StringComparison.Ordinal)
            < message.IndexOf("met_hale", System.StringComparison.Ordinal));
    }

    [Fact]
    public void ListSaysSoWhenNothingIsSet() =>
        Assert.Contains("No world flags", EditorFlagCommands.List(new GameState()).Message);

    [Theory]
    [InlineData("set")]
    [InlineData("clear")]
    [InlineData("get")]
    public void ASubcommandWithoutANameFails(string sub)
    {
        var result = Run(new GameState(), sub);
        Assert.False(result.Success);
        Assert.Contains("Usage", result.Message);
    }

    [Fact]
    public void AColonInAFlagNameIsRefusedBecauseDialogueCouldNotReferenceIt()
    {
        var result = Run(new GameState(), "set", "met:hale");
        Assert.False(result.Success);
        Assert.Contains("can't contain", result.Message);
    }

    [Fact]
    public void AnEmptyValueIsRefusedInFavourOfClear()
    {
        var result = Run(new GameState(), "set", "met_hale", "");
        Assert.False(result.Success);
        Assert.Contains("flag clear", result.Message);
    }

    [Fact]
    public void AnUnknownSubcommandAndAMissingGameBothExplainThemselves()
    {
        var unknown = Run(new GameState(), "toggle", "met_hale");
        Assert.False(unknown.Success);
        Assert.Contains("Unknown flag subcommand", unknown.Message);

        Assert.False(Run(null, "set", "met_hale").Success);
        Assert.Contains("No game in progress", EditorFlagCommands.List(null).Message);
    }
}
