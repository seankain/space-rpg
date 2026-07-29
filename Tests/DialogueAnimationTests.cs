using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

// Yarn plan Phase 4: the play_anim verb — the one effect whose only consequence
// is what the player sees. The curated clip list is the interesting part: it is
// an engine-free vocabulary naming assets on disk, so the test that matters
// most is the one tying the two together.
public class DialogueAnimationTests
{
    private sealed class FakeHost : IDialogueEffectHost
    {
        public readonly List<string> Animations = new();

        public void StartBattle(bool despawnOnDefeat)
        {
        }

        public void OpenShop()
        {
        }

        public void Recruit(ulong partyCharacterId)
        {
        }

        public void PlayAnimation(string clip, bool loop) =>
            Animations.Add(loop ? $"{clip}:loop" : clip);
    }

    private static (FakeHost host, List<string> warnings) Run(string token)
    {
        var host = new FakeHost();
        var warnings = new List<string>();
        DialogueEffects.Run(EffectRef.Parse(token), new DialogueContext
        {
            State = new GameState(),
            Host = host,
            LogWarning = warnings.Add,
        });
        return (host, warnings);
    }

    [Fact]
    public void EveryCuratedClipExistsInTheAnimationLibrary()
    {
        // The vocabulary can't scan res:// — it is engine-free — so this is
        // what stops a typo or a deleted asset from turning into a gesture that
        // silently never plays.
        var directory = ClipDirectory();
        var missing = DialogueAnimations.Ids
            .Where(id => !File.Exists(Path.Combine(directory, id + ".res")))
            .ToArray();
        Assert.Equal(Array.Empty<string>(), missing);
    }

    [Fact]
    public void TheCuratedListHasNoDuplicatesAndNoBlanks()
    {
        Assert.Equal(DialogueAnimations.Ids.Length, DialogueAnimations.Ids.Distinct().Count());
        Assert.DoesNotContain(DialogueAnimations.Ids, string.IsNullOrWhiteSpace);
    }

    [Fact]
    public void ResolveIsCaseInsensitiveAndRejectsClipsOutsideTheList()
    {
        Assert.Equal("Waving", DialogueAnimations.Resolve("waving"));
        Assert.Equal("Waving", DialogueAnimations.Resolve("Waving"));
        // A real clip in the library that isn't a conversation gesture.
        Assert.Null(DialogueAnimations.Resolve("Swim_Fwd"));
        Assert.Null(DialogueAnimations.Resolve("Wavin"));
        Assert.Null(DialogueAnimations.Resolve(""));
        Assert.Null(DialogueAnimations.Resolve(null));
    }

    [Fact]
    public void PlayAnimReachesTheHostOnceAndLoopsOnlyWhenAsked()
    {
        var (once, warnings) = Run("play_anim:Waving");
        Assert.Equal(new[] { "Waving" }, once.Animations);
        Assert.Empty(warnings);

        var (looping, _) = Run("play_anim:Sword_Idle:loop");
        Assert.Equal(new[] { "Sword_Idle:loop" }, looping.Animations);
    }

    [Fact]
    public void TheHostGetsTheCanonicalSpelling()
    {
        var (host, _) = Run("play_anim:cheering");
        Assert.Equal(new[] { "Cheering" }, host.Animations);
    }

    [Fact]
    public void AnUnknownClipIsReportedAndNothingIsPlayed()
    {
        var (host, warnings) = Run("play_anim:Moonwalk");
        Assert.Empty(host.Animations);
        Assert.Contains(warnings, w => w.Contains("unknown clip 'Moonwalk'"));
    }

    [Fact]
    public void WithoutASceneHostItSaysSoInsteadOfThrowing()
    {
        var warnings = new List<string>();
        DialogueEffects.Run(EffectRef.Parse("play_anim:Waving"), new DialogueContext
        {
            State = new GameState(),
            LogWarning = warnings.Add,
        });
        Assert.Contains(warnings, w => w.Contains("needs a scene host"));
    }

    [Theory]
    [InlineData("play_anim", "play_anim takes")]
    [InlineData("play_anim:Waving:twice", "second argument is 'loop'")]
    [InlineData("play_anim:Waving:loop:more", "play_anim takes")]
    [InlineData("play_anim:Swim_Fwd", "is not a dialogue clip")]
    public void BadArgsAreRejectedByTheValidator(string token, string expected) =>
        Assert.Contains(expected, DialogueEffects.Validate(EffectRef.Parse(token)));

    [Theory]
    [InlineData("play_anim:Waving")]
    [InlineData("play_anim:waving")]
    [InlineData("play_anim:Sword_Idle:loop")]
    public void GoodArgsPassTheValidator(string token) =>
        Assert.Null(DialogueEffects.Validate(EffectRef.Parse(token)));

    [Fact]
    public void ItIsWritableInYarnAndSurvivesARoundTrip()
    {
        var source = @"
title: greet
---
<<play_anim Sword_Idle loop>>
$npc: Turn around, groundsider.
-> Hand it over
    <<play_anim PickUp>>
    <<stop>>
===
";
        var problems = new List<string>();
        var graph = YarnGraphCompiler.Compile("test.anim", source, problems);
        Assert.Empty(problems);

        var greet = graph.GetNode("greet");
        Assert.Equal(new[] { "play_anim:Sword_Idle:loop" }, greet.OnShownEffects.Select(e => e.ToToken()));
        Assert.Equal(new[] { "play_anim:PickUp" }, greet.Choices[0].Effects.Select(e => e.ToToken()));

        var rewritten = YarnGraphCompiler.Compile(
            "test.anim", YarnGraphWriter.Write(graph, problems), problems);
        Assert.Empty(problems);
        Assert.Equal(
            new[] { "play_anim:Sword_Idle:loop" },
            rewritten.GetNode("greet").OnShownEffects.Select(e => e.ToToken()));
    }

    private static string ClipDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "ThirdParty", "AnimationLibrary");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new DirectoryNotFoundException("Could not locate ThirdParty/AnimationLibrary above the test assembly.");
    }
}
