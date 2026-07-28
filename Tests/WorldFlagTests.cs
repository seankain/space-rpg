using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

// Yarn plan Phase 3 acceptance: the world-flag store on GameState, the flag /
// set_flag dialogue vocabulary built on it, and the promise that a flag a
// conversation sets survives a save and reload. The two stat/party conditions
// that landed with it are here too — they read the same GameState the same way.
public class WorldFlagTests
{
    private static DialogueContext Context(GameState state, out List<string> warnings)
    {
        var captured = new List<string>();
        warnings = captured;
        return new DialogueContext
        {
            State = state,
            SpeakerName = "Moss",
            LogWarning = captured.Add,
        };
    }

    private static bool Holds(string token, GameState state)
    {
        var context = Context(state, out var warnings);
        var result = DialogueConditions.Evaluate(ConditionRef.Parse(token), context);
        Assert.Empty(warnings);
        return result;
    }

    private static void Run(string token, GameState state)
    {
        var context = Context(state, out var warnings);
        DialogueEffects.Run(EffectRef.Parse(token), context);
        Assert.Empty(warnings);
    }

    // --- The store -----------------------------------------------------------

    [Fact]
    public void AFlagIsUnsetUntilItIsSet()
    {
        var state = new GameState();
        Assert.Null(state.GetFlag("met_hale"));
        Assert.False(state.IsFlagSet("met_hale"));

        state.SetFlag("met_hale", "true");
        Assert.True(state.IsFlagSet("met_hale"));
        Assert.Equal("true", state.GetFlag("met_hale"));
    }

    [Fact]
    public void AFlagCarriesAValueNotJustAYesOrNo()
    {
        var state = new GameState();
        state.SetFlag("hale_mood", "angry");
        Assert.Equal("angry", state.GetFlag("hale_mood"));
        Assert.True(state.IsFlagSet("hale_mood"));
    }

    [Theory]
    // "Set" means "holds something that isn't an explicit no", so a script that
    // writes false or 0 reads back as not set rather than as a funny value.
    [InlineData("true", true)]
    [InlineData("angry", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("0", false)]
    public void TruthinessTreatsFalseAndZeroAsUnset(string value, bool expected)
    {
        var state = new GameState();
        state.SetFlag("f", value);
        Assert.Equal(expected, state.IsFlagSet("f"));
        // The raw value is still readable, which is what flag(name, value) uses.
        Assert.Equal(value, state.GetFlag("f"));
    }

    [Fact]
    public void ClearingRemovesTheEntryRatherThanStoringAnEmptyOne()
    {
        var state = new GameState();
        state.SetFlag("f", "true");
        state.ClearFlag("f");
        Assert.Empty(state.Flags);

        // Setting an empty value is the same as clearing.
        state.SetFlag("f", "true");
        state.SetFlag("f", "");
        Assert.Empty(state.Flags);
    }

    [Fact]
    public void FlagNamesAreNormalizedSoCaseAndPaddingCannotForkAFlag()
    {
        var state = new GameState();
        state.SetFlag("  Met_Hale ", "true");
        Assert.Equal("met_hale", Assert.Single(state.Flags).Key);
        Assert.True(state.IsFlagSet("MET_HALE"));
        Assert.True(state.IsFlagSet("met_hale"));
    }

    [Fact]
    public void AnEmptyFlagNameIsIgnored()
    {
        var state = new GameState();
        state.SetFlag("   ", "true");
        state.SetFlag(null, "true");
        Assert.Empty(state.Flags);
        Assert.False(state.IsFlagSet(null));
    }

    // --- Saves ---------------------------------------------------------------

    [Fact]
    public void FlagsSurviveASaveAndReload()
    {
        var root = Path.Combine(Path.GetTempPath(), $"spacerpg-flags-{Guid.NewGuid():N}");
        try
        {
            var repository = new SaveRepository(root);
            var state = new GameState { CurrentLevelPath = "res://Scenes/Levels/Intro.tscn" };
            state.SetFlag("met_shopkeeper", "true");
            state.SetFlag("hale_mood", "angry");

            var slotId = Guid.NewGuid();
            repository.Save(
                new SaveData { SlotId = slotId, SaveVersion = SaveRepository.CurrentSaveVersion }, state);
            var loaded = repository.LoadState(slotId);

            Assert.True(loaded.IsFlagSet("met_shopkeeper"));
            Assert.Equal("angry", loaded.GetFlag("hale_mood"));
            // Normalization still applies to a dictionary rebuilt by the
            // deserializer, which is why the accessors normalize instead of a
            // comparer.
            Assert.True(loaded.IsFlagSet("MET_SHOPKEEPER"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ASaveFromBeforeFlagsExistedLoadsWithNone()
    {
        var root = Path.Combine(Path.GetTempPath(), $"spacerpg-flags-{Guid.NewGuid():N}");
        try
        {
            var repository = new SaveRepository(root);
            var slotId = Guid.NewGuid();
            var directory = Path.Combine(root, $"slot_{slotId:N}");
            Directory.CreateDirectory(directory);
            // A v7 state.json: everything Flags was added beside, and no Flags.
            File.WriteAllText(
                Path.Combine(directory, "state.json"),
                "{\"CurrentLevelPath\":\"res://Scenes/Levels/Intro.tscn\",\"DefeatedNpcs\":[\"intro.vex\"]}");
            File.WriteAllText(
                Path.Combine(directory, "meta.json"),
                $"{{\"SaveVersion\":7,\"SlotId\":\"{slotId}\"}}");

            var loaded = repository.LoadState(slotId);
            Assert.NotNull(loaded.Flags);
            Assert.Empty(loaded.Flags);
            Assert.False(loaded.IsFlagSet("met_shopkeeper"));
            Assert.True(loaded.IsNpcDefeated("intro.vex"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    // --- The vocabulary ------------------------------------------------------

    [Fact]
    public void SetFlagWritesTheFlagAndDefaultsToTrue()
    {
        var state = new GameState();
        Run("set_flag:met_hale", state);
        Assert.Equal("true", state.GetFlag("met_hale"));

        Run("set_flag:hale_mood:angry", state);
        Assert.Equal("angry", state.GetFlag("hale_mood"));

        // An explicit empty value clears, matching GameState.SetFlag.
        Run("set_flag:met_hale:", state);
        Assert.False(state.IsFlagSet("met_hale"));
    }

    [Fact]
    public void FlagAsksWhetherAFlagIsSetOrHoldsAValue()
    {
        var state = new GameState();
        Assert.False(Holds("flag:met_hale", state));

        state.SetFlag("met_hale", "true");
        Assert.True(Holds("flag:met_hale", state));

        state.SetFlag("hale_mood", "angry");
        Assert.True(Holds("flag:hale_mood:angry", state));
        Assert.True(Holds("flag:hale_mood:ANGRY", state));
        Assert.False(Holds("flag:hale_mood:pleased", state));
    }

    [Fact]
    public void AnEmptyExpectedValueAsksWhetherTheFlagIsUnset()
    {
        // The vocabulary has no negation, so this is how a script writes
        // "hasn't met me yet" as a condition rather than a router fallback.
        var state = new GameState();
        Assert.True(Holds("flag:met_hale:", state));
        state.SetFlag("met_hale", "true");
        Assert.False(Holds("flag:met_hale:", state));
    }

    [Fact]
    public void PartySizeAsksForAtLeastThatMany()
    {
        var state = new GameState();
        state.Party.Add(Member(1));
        Assert.True(Holds("party_size:1", state));
        Assert.False(Holds("party_size:2", state));

        state.Party.Add(Member(2));
        Assert.True(Holds("party_size:2", state));
    }

    [Fact]
    public void StatGatesOnTheLeadersStat()
    {
        var state = new GameState();
        var leader = Member(1);
        leader.Stats.Charisma = 12;
        leader.Stats.Strength = 4;
        state.Party.Add(leader);
        // A second member's numbers must not decide the leader's line.
        var follower = Member(2);
        follower.Stats.Charisma = 99;
        state.Party.Add(follower);

        Assert.True(Holds("stat:Charisma:12", state));
        Assert.True(Holds("stat:charisma:10", state));
        Assert.False(Holds("stat:Charisma:13", state));
        Assert.False(Holds("stat:Strength:10", state));
    }

    [Fact]
    public void StatWithNoPartyHidesTheBranchAndSaysWhy()
    {
        var context = Context(new GameState(), out var warnings);
        Assert.False(DialogueConditions.Evaluate(ConditionRef.Parse("stat:Charisma:1"), context));
        Assert.Contains(warnings, w => w.Contains("no party leader"));
    }

    [Theory]
    [InlineData("set_flag", "set_flag takes")]
    [InlineData("set_flag:", "the flag name is empty")]
    [InlineData("set_flag:a:b:c", "set_flag takes")]
    public void BadSetFlagArgsAreRejectedByTheValidator(string token, string expected)
    {
        Assert.Contains(expected, DialogueEffects.Validate(EffectRef.Parse(token)));
    }

    [Fact]
    public void AColonInAFlagNameIsRejected()
    {
        // Args are colon-joined in the compact token form, so an argument
        // containing one would not survive the round trip. Only a hand-built
        // ref (or `<<set_flag "met:hale">>` in Yarn) can get here — parsing
        // "set_flag:met:hale" just yields two arguments.
        Assert.Contains(
            "can't contain",
            DialogueEffects.Validate(new EffectRef { Id = "set_flag", Args = new[] { "met:hale" } }));
        Assert.Contains(
            "can't contain",
            DialogueConditions.Validate(new ConditionRef { Id = "flag", Args = new[] { "met:hale" } }));
    }

    [Theory]
    [InlineData("flag", "flag takes")]
    [InlineData("flag:", "the flag name is empty")]
    [InlineData("party_size", "party_size takes")]
    [InlineData("party_size:lots", "not a whole number")]
    [InlineData("stat:Charisma", "stat takes")]
    [InlineData("stat:Charm:10", "is not a stat")]
    [InlineData("stat:Charisma:high", "not a whole number")]
    public void BadConditionArgsAreRejectedByTheValidator(string token, string expected)
    {
        Assert.Contains(expected, DialogueConditions.Validate(ConditionRef.Parse(token)));
    }

    [Theory]
    [InlineData("set_flag:met_hale")]
    [InlineData("set_flag:hale_mood:angry")]
    public void GoodSetFlagArgsPassTheValidator(string token) =>
        Assert.Null(DialogueEffects.Validate(EffectRef.Parse(token)));

    [Theory]
    [InlineData("flag:met_hale")]
    [InlineData("flag:hale_mood:angry")]
    [InlineData("party_size:2")]
    [InlineData("stat:Charisma:12")]
    public void GoodConditionArgsPassTheValidator(string token) =>
        Assert.Null(DialogueConditions.Validate(ConditionRef.Parse(token)));

    // --- Authoring them in Yarn ---------------------------------------------

    [Fact]
    public void TheNewVerbsAreWritableInYarnAndSurviveARoundTrip()
    {
        var source = @"
title: greet
---
<<if flag(""met_hale"")>>
    <<jump again>>
<<endif>>
<<set_flag met_hale>>
$npc: New face on my deck.
-> Talk your way past (Charisma) <<if stat(""Charisma"", 12)>>
    <<set_flag hale_mood ""charmed"">>
    <<stop>>
-> Nothing to say
    <<stop>>
===

title: again
---
$npc: You again.
===
";
        var problems = new List<string>();
        var graph = YarnGraphCompiler.Compile("test.flags", source, problems);
        Assert.Empty(problems);

        var router = graph.GetNode("greet");
        Assert.Equal("flag:met_hale", router.Branches[0].When.ToToken());

        var line = graph.GetNode(router.NextNodeId);
        Assert.Equal(new[] { "set_flag:met_hale" }, line.OnShownEffects.Select(e => e.ToToken()));
        Assert.Equal("stat:Charisma:12", line.Choices[0].Visible.ToToken());
        Assert.Equal(new[] { "set_flag:hale_mood:charmed" }, line.Choices[0].Effects.Select(e => e.ToToken()));

        // And they come back out of the writer unchanged.
        var rewritten = YarnGraphCompiler.Compile(
            "test.flags", YarnGraphWriter.Write(graph, problems), problems);
        Assert.Empty(problems);
        var rewrittenLine = rewritten.GetNode(rewritten.GetNode("greet").NextNodeId);
        Assert.Equal("stat:Charisma:12", rewrittenLine.Choices[0].Visible.ToToken());
        Assert.Equal(
            new[] { "set_flag:hale_mood:charmed" },
            rewrittenLine.Choices[0].Effects.Select(e => e.ToToken()));
    }

    private static CharacterEntity Member(ulong id) => new()
    {
        Id = id,
        Name = $"Member {id}",
        Level = 1,
        HealthPoints = 10,
        MaxHealthPoints = 10,
        Stats = new CharacterStats(),
        EquipSlots = new CharacterEquipSlots(),
        ActiveStatusEffects = new List<ActiveStatusEffect>(),
    };
}
