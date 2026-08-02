using System.Collections.Generic;
using Xunit;

// dialogue-state-conditions plan Phase 1: the value-returning half of the
// condition vocabulary. A conversation could always spend credits and never ask
// about them, because every verb was a predicate with its comparison baked in.
// These cover the queries themselves, the comparison operators they're read
// through, negation, and the promise that the tokens written before the plan
// still mean exactly what they meant.
public class DialogueQueryTests
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

    // Evaluates a token and insists the vocabulary had nothing to complain about.
    private static bool Holds(string token, GameState state)
    {
        var context = Context(state, out var warnings);
        var result = DialogueConditions.Evaluate(ConditionRef.Parse(token), context);
        Assert.Empty(warnings);
        return result;
    }

    private static GameState WithLeader(uint credits = 0)
    {
        var state = new GameState { Credits = credits };
        state.Party.Add(new CharacterEntity
        {
            Id = 1,
            Name = "Leader",
            Level = 3,
            HealthPoints = 7,
            MaxHealthPoints = 20,
            Stats = new CharacterStats { Charisma = 12, Strength = 4 },
            EquipSlots = new CharacterEquipSlots(),
            ActiveStatusEffects = new List<ActiveStatusEffect>(),
        });
        return state;
    }

    // --- The queries ---------------------------------------------------------

    [Fact]
    public void CreditsGateOnTheBalanceTheConversationCanSee()
    {
        // The case the plan exists for: a docking fee the player can't afford.
        var state = new GameState { Credits = 150 };
        Assert.False(Holds("credits:>=:200", state));

        state.Credits = 200;
        Assert.True(Holds("credits:>=:200", state));
        Assert.True(Holds("credits:>:199", state));
        Assert.False(Holds("credits:>:200", state));
        Assert.True(Holds("credits:==:200", state));
        Assert.True(Holds("credits:!=:199", state));
        Assert.True(Holds("credits:<=:200", state));
        Assert.False(Holds("credits:<:200", state));
    }

    [Fact]
    public void CreditsFollowTheEffectThatSpendsThem()
    {
        // The read is live, not a snapshot: what <<credits -200>> leaves behind
        // is what the next evaluation sees.
        var state = new GameState { Credits = 250 };
        var context = Context(state, out _);
        Assert.True(DialogueConditions.Evaluate(ConditionRef.Parse("credits:>=:200"), context));

        DialogueEffects.Run(EffectRef.Parse("credits:-200"), context);
        Assert.Equal(50u, state.Credits);
        Assert.False(DialogueConditions.Evaluate(ConditionRef.Parse("credits:>=:200"), context));
    }

    [Fact]
    public void ItemCountComparesTheStackNotJustItsPresence()
    {
        var state = new GameState();
        state.Inventory.Add(ItemCatalog.MaguffinCubeId, 3);
        Assert.True(Holds($"item_count:{ItemCatalog.MaguffinCubeId}:==:3", state));
        Assert.True(Holds($"item_count:{ItemCatalog.MaguffinCubeId}:>=:2", state));
        Assert.False(Holds($"item_count:{ItemCatalog.MaguffinCubeId}:>:3", state));
        // The thing has_item could never ask: an empty pocket, without a `not`.
        Assert.False(Holds($"item_count:{ItemCatalog.MaguffinCubeId}:==:0", state));
    }

    [Fact]
    public void PartyAndLeaderQueriesReadTheRoster()
    {
        var state = WithLeader();
        Assert.True(Holds("party_size:>=:1", state));
        Assert.False(Holds("party_size:>=:2", state));
        Assert.True(Holds("stat:Charisma:>=:12", state));
        Assert.False(Holds("stat:Strength:>=:12", state));
        Assert.True(Holds("level:==:3", state));
        Assert.True(Holds("health:<:10", state));
        Assert.True(Holds("max_health:==:20", state));
    }

    [Fact]
    public void TextQueriesCompareForEqualityOnly()
    {
        var state = new GameState();
        state.SetQuestState(QuestCatalog.ReturnTheMaguffinId, QUESTSUCCESSSTATE.InProgress);
        state.SetFlag("hale_mood", "angry");

        Assert.True(Holds($"quest:{QuestCatalog.ReturnTheMaguffinId}:==:InProgress", state));
        Assert.True(Holds($"quest:{QuestCatalog.ReturnTheMaguffinId}:!=:Success", state));
        Assert.True(Holds("flag_value:hale_mood:==:angry", state));
        // Case-insensitive, matching how flag() has always compared.
        Assert.True(Holds("flag_value:hale_mood:==:ANGRY", state));
        Assert.False(Holds("flag_value:hale_mood:==:calm", state));
        // An unset flag reads as empty rather than failing.
        Assert.True(Holds("flag_value:never_set:==:", state));
    }

    [Fact]
    public void OrderingTextIsRefusedRatherThanGuessedAt()
    {
        var state = new GameState();
        var context = Context(state, out var warnings);
        Assert.False(DialogueConditions.Evaluate(ConditionRef.Parse("flag_value:mood:>:angry"), context));
        Assert.Contains(warnings, w => w.Contains("== and !="));
        Assert.Contains("compares with == or != only", DialogueConditions.Validate(
            ConditionRef.Parse("flag_value:mood:>:angry")));
    }

    // --- Negation ------------------------------------------------------------

    [Fact]
    public void NotInvertsAnyCondition()
    {
        var state = new GameState { Credits = 10 };
        Assert.True(Holds("!credits:>=:200", state));
        Assert.False(Holds("!credits:>=:1", state));

        state.SetFlag("met_hale", "true");
        Assert.False(Holds("!flag:met_hale", state));
        Assert.True(Holds("!flag:never_met", state));
        Assert.True(Holds($"!has_item:{ItemCatalog.MaguffinCubeId}", state));
    }

    [Fact]
    public void ANegatedConditionThatCannotBeEvaluatedStillHides()
    {
        // The safety property behind returning null rather than false from a
        // failed read: a bad edit must only ever close a path, never open one.
        var context = Context(new GameState(), out var warnings);
        Assert.False(DialogueConditions.Evaluate(ConditionRef.Parse("!nonsense:1"), context));
        Assert.False(DialogueConditions.Evaluate(ConditionRef.Parse("!stat:Charisma:>=:1"), context));
        Assert.Contains(warnings, w => w.Contains("Unknown dialogue condition"));
        Assert.Contains(warnings, w => w.Contains("no party leader"));
    }

    [Fact]
    public void ALoneBangIsNotANegation()
    {
        var context = Context(new GameState(), out var warnings);
        Assert.False(DialogueConditions.Evaluate(new ConditionRef { Id = "!" }, context));
        Assert.Contains(warnings, w => w.Contains("Unknown dialogue condition"));
    }

    // --- Compatibility with what was already authored -------------------------

    [Fact]
    public void LegacyAtLeastTokensStillMeanAtLeast()
    {
        // party_size:2 and stat:Charisma:12 predate the operator. An elided
        // operator is the kind's default, so they keep meaning ">=" — which is
        // what makes this plan a no-migration change.
        var state = WithLeader();
        Assert.True(Holds("party_size:1", state));
        Assert.False(Holds("party_size:2", state));
        Assert.True(Holds("stat:Charisma:12", state));
        Assert.False(Holds("stat:Charisma:13", state));
        Assert.True(Holds("credits:0", state));

        Assert.Null(DialogueConditions.Validate(ConditionRef.Parse("party_size:2")));
        Assert.Null(DialogueConditions.Validate(ConditionRef.Parse("stat:Charisma:12")));
    }

    [Fact]
    public void AnElidedOperatorOnATextQueryMeansEquals()
    {
        var state = new GameState();
        state.SetQuestState(QuestCatalog.ReturnTheMaguffinId, QUESTSUCCESSSTATE.Success);
        Assert.True(Holds($"quest:{QuestCatalog.ReturnTheMaguffinId}:Success", state));
        Assert.False(Holds($"quest:{QuestCatalog.ReturnTheMaguffinId}:InProgress", state));
    }

    [Fact]
    public void ThePredicateVerbsAreUntouched()
    {
        var state = WithLeader(500);
        state.Inventory.Add(ItemCatalog.MaguffinCubeId, 1);
        state.SetFlag("met_hale", "true");
        state.MarkNpcDefeated("intro.vex");
        state.SetQuestState(QuestCatalog.ReturnTheMaguffinId, QUESTSUCCESSSTATE.InProgress);

        Assert.True(Holds($"has_item:{ItemCatalog.MaguffinCubeId}", state));
        Assert.True(Holds("flag:met_hale", state));
        Assert.True(Holds("npc_defeated:intro.vex", state));
        Assert.True(Holds("party_has_room", state));
        Assert.True(Holds($"quest_state:{QuestCatalog.ReturnTheMaguffinId}:InProgress", state));
    }

    // --- Validation ----------------------------------------------------------

    [Theory]
    [InlineData("credits", "must be compared to a value")]
    [InlineData("credits:lots", "is not a number")]
    [InlineData("credits:>=:lots", "is not a number")]
    [InlineData("item_count:>=:1", "item_count is written")]
    [InlineData("item_count:999:>=:1", "no item with id 999")]
    [InlineData("item_count:cube:>=:1", "is not a whole number")]
    [InlineData("quest:999:==:Success", "no quest with id 999")]
    [InlineData("stat:Charm:>=:1", "is not a stat")]
    [InlineData("flag_value:>:x", "flag_value is written")]
    [InlineData("health:>=:x", "is not a number")]
    public void BadComparisonsAreRejectedByTheValidator(string token, string expected) =>
        Assert.Contains(expected, DialogueConditions.Validate(ConditionRef.Parse(token)));

    [Theory]
    [InlineData("credits:>=:200")]
    [InlineData("credits:200")]
    [InlineData("!credits:<:200")]
    [InlineData("item_count:1:==:0")]
    [InlineData("stat:Charisma:>:11")]
    [InlineData("party_size:<=:4")]
    [InlineData("quest:1:!=:Success")]
    [InlineData("flag_value:hale_mood:==:angry")]
    [InlineData("health:<=:0")]
    [InlineData("level:>=:5")]
    [InlineData("max_health:>=:1")]
    [InlineData("!has_item:1")]
    public void GoodComparisonsPassTheValidator(string token) =>
        Assert.Null(DialogueConditions.Validate(ConditionRef.Parse(token)));

    [Fact]
    public void EveryQueryIsOfferedAsACondition()
    {
        // The editor's dropdown and the compiler's "known conditions" list both
        // read DialogueConditions.Ids, so a query missing from it would be
        // evaluable but unauthorable.
        foreach (var id in DialogueQueries.Ids)
        {
            Assert.Contains(id, DialogueConditions.Ids);
        }
    }

    // --- In a conversation ---------------------------------------------------

    [Fact]
    public void ACreditGateRunsThroughTheRuntime()
    {
        var source = @"
title: toll
---
$npc: Berth's two hundred credits. Cash up front.
-> Pay the fee <<if credits() >= 200>>
    <<credits -200>>
    <<jump paid>>
-> I'll dock elsewhere
    <<stop>>
===

title: paid
---
$npc: Clamps are off. Welcome to the deck.
===
";
        var problems = new List<string>();
        var graph = YarnGraphCompiler.Compile("test.toll", source, problems);
        Assert.Empty(problems);

        var broke = new GameState { Credits = 150 };
        var line = DialogueRuntime.Compile(graph, Context(broke, out _));
        Assert.Single(line.Choices);
        Assert.Equal("I'll dock elsewhere", line.Choices[0].Label);

        var flush = new GameState { Credits = 250 };
        var context = Context(flush, out _);
        line = DialogueRuntime.Compile(graph, context);
        Assert.Equal(2, line.Choices.Count);
        Assert.Equal("Pay the fee", line.Choices[0].Label);

        line.Choices[0].Action();
        Assert.Equal(50u, flush.Credits);
        Assert.Equal("Clamps are off. Welcome to the deck.", line.Choices[0].Next.Text);
    }

    // --- Phase 2: conditions read when the node is reached --------------------

    [Fact]
    public void ABranchAfterAPaymentSeesThePostPaymentBalance()
    {
        // The Phase 2 acceptance. Before conditions were resolved lazily, the
        // `paid` router was evaluated when the conversation started and took the
        // "still flush" branch no matter what the player had just handed over.
        var source = @"
title: toll
---
$npc: Berth's two hundred.
-> Pay <<if credits() >= 200>>
    <<credits -200>>
    <<jump paid>>
===

title: paid
---
<<if credits() >= 200>>
    <<jump rich>>
<<else>>
    <<jump broke>>
<<endif>>
===

title: rich
---
$npc: Still flush, I see.
===

title: broke
---
$npc: That's you cleaned out, then.
===
";
        var problems = new List<string>();
        var graph = YarnGraphCompiler.Compile("test.toll2", source, problems);
        Assert.Empty(problems);

        // 250 in, 200 out: the branch on the far side must see 50.
        var state = new GameState { Credits = 250 };
        var line = DialogueRuntime.Compile(graph, Context(state, out _));
        var pay = line.Choices[0];
        pay.Action();
        Assert.Equal("That's you cleaned out, then.", pay.Next.Text);

        // And with enough left over it still takes the other branch.
        var rich = new GameState { Credits = 500 };
        var richLine = DialogueRuntime.Compile(graph, Context(rich, out _));
        var richPay = richLine.Choices[0];
        richPay.Action();
        Assert.Equal("Still flush, I see.", richPay.Next.Text);
    }

    [Fact]
    public void AGateOnALaterNodeSeesWhatAnEarlierLineDid()
    {
        // The same promise for a choice gate rather than a router, and for a
        // line's own effects: DialogueManager runs OnShown before it reads the
        // choices, so a gate on that line sees what the line just changed.
        var source = @"
title: start
---
$npc: Here, take this.
-> Go on
    <<jump gift>>
===

title: gift
---
<<give_item 1>>
$npc: Mind how you carry it.
-> Hand it straight back <<if has_item(1)>>
    <<take_item 1>>
    <<stop>>
-> Keep it
    <<stop>>
===
";
        var problems = new List<string>();
        var graph = YarnGraphCompiler.Compile("test.gift", source, problems);
        Assert.Empty(problems);

        var state = new GameState();
        var line = DialogueRuntime.Compile(graph, Context(state, out _));
        var gift = line.Choices[0].Next;
        // The cube arrives as the line shows; only then are its choices read.
        gift.OnShown();
        Assert.Equal(2, gift.Choices.Count);
        Assert.Equal("Hand it straight back", gift.Choices[0].Label);
    }

    [Fact]
    public void ANodeReachedTwiceIsGatedAfreshEachTime()
    {
        var source = @"
title: desk
---
$npc: Anything else?
-> Buy a ration (10cr) <<if credits() >= 10>>
    <<credits -10>>
    <<jump desk>>
-> Nothing
    <<stop>>
===
";
        var problems = new List<string>();
        var graph = YarnGraphCompiler.Compile("test.desk", source, problems);
        Assert.Empty(problems);

        var state = new GameState { Credits = 15 };
        var line = DialogueRuntime.Compile(graph, Context(state, out _));

        // First time round the ration is affordable...
        Assert.Equal(2, line.Choices.Count);
        line.Choices[0].Action();
        Assert.Equal(5u, state.Credits);

        // ...and coming back to the same node with 5 left, it isn't offered.
        var again = line.Choices[0].Next;
        Assert.Single(again.Choices);
        Assert.Equal("Nothing", again.Choices[0].Label);
    }

    [Fact]
    public void EffectsStillRunOnceNoMatterHowOftenTheLineIsRead()
    {
        // Laziness changes when conditions are read, never how often effects
        // fire: the resolved answer is remembered on the line.
        var source = @"
title: start
---
<<credits 100>>
$npc: On the house.
-> Thanks
    <<stop>>
===
";
        var problems = new List<string>();
        var graph = YarnGraphCompiler.Compile("test.once", source, problems);
        Assert.Empty(problems);

        var state = new GameState { Credits = 0 };
        var line = DialogueRuntime.Compile(graph, Context(state, out _));
        line.OnShown();
        Assert.Equal(100u, state.Credits);

        // Re-reading the line's choices (the box renders them, the input
        // handler checks them) must not re-run anything.
        Assert.Same(line.Choices, line.Choices);
        Assert.Equal(100u, state.Credits);
    }
}
