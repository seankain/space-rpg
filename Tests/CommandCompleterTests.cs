using System.Collections.Generic;
using Xunit;

// In-game-editor plan Phase 6: Tab-completion of the final console token. The
// DevConsole candidate selection and key handling are Godot-side; this covers
// the engine-free completion math.
public class CommandCompleterTests
{
    private static readonly string[] Commands =
        { "give", "giveitem", "goto", "echo", "editor", "edit", "quest", "quests" };

    private static Completion Complete(string line, params string[] candidates) =>
        CommandCompleter.CompleteLine(line, candidates);

    [Fact]
    public void SoleMatchCompletesAndAppendsSpace()
    {
        var result = Complete("got", Commands);
        Assert.Equal("goto ", result.Line);
        Assert.Single(result.Matches);
    }

    [Fact]
    public void SeveralMatchesExtendToCommonPrefixOnly()
    {
        // "give" and "giveitem" share the prefix "give"; no trailing space.
        var result = Complete("gi", Commands);
        Assert.Equal("give", result.Line);
        Assert.Equal(2, result.Matches.Count);
    }

    [Fact]
    public void CommonPrefixStopsAtFirstDivergence()
    {
        // echo / editor / edit -> only "e" is shared (echo vs edit*).
        var result = Complete("e", Commands);
        Assert.Equal("e", result.Line);
        Assert.Equal(3, result.Matches.Count);
    }

    [Fact]
    public void NoMatchLeavesLineUnchanged()
    {
        var result = Complete("zzz", Commands);
        Assert.Equal("zzz", result.Line);
        Assert.Empty(result.Matches);
    }

    [Fact]
    public void CompletionIsCaseInsensitive()
    {
        var result = Complete("GO", Commands);
        Assert.Equal("goto ", result.Line);
    }

    [Fact]
    public void OnlyTheFinalTokenIsCompleted()
    {
        // The head ("quest ") is preserved; the second token completes.
        var result = CommandCompleter.CompleteLine("quest st", new[] { "start", "set", "stage", "advance" });
        Assert.Equal("quest sta", result.Line); // start + stage share "sta"
        Assert.Equal(2, result.Matches.Count);
    }

    [Fact]
    public void TrailingSpaceCompletesANewTokenAgainstAllCandidates()
    {
        var result = CommandCompleter.CompleteLine("give ", new[] { "1", "2", "3" });
        // No common prefix among 1/2/3, so the line is unchanged but all match.
        Assert.Equal("give ", result.Line);
        Assert.Equal(3, result.Matches.Count);
    }

    [Fact]
    public void SingleCandidateAfterSpaceCompletesFully()
    {
        var result = CommandCompleter.CompleteLine("list ", new[] { "npcs" });
        Assert.Equal("list npcs ", result.Line);
    }
}
