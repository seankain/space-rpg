using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

// The automated half of the npc-dialogue-yarn.md Phase 2 regression pass: plays
// the committed intro conversations — now authored as .yarn — through the real
// DialogueRuntime against real GameStates and walks the branches that matter.
// A playthrough is still the other half (nothing here presses E), but a
// conversion that quietly loses a quest state, an item, or the battle branch
// fails right here instead of in a play session.
public class IntroDialogueBranchTests
{
    // Records the scene-facing verbs, which have no engine-free implementation.
    private sealed class FakeHost : IDialogueEffectHost
    {
        public int Battles;
        public bool LastBattleDespawned;
        public int Shops;
        public readonly List<ulong> Recruited = new();

        public void StartBattle(bool despawnOnDefeat)
        {
            Battles++;
            LastBattleDespawned = despawnOnDefeat;
        }

        public void OpenShop() => Shops++;

        public void Recruit(ulong partyCharacterId) => Recruited.Add(partyCharacterId);

        public readonly List<string> Animations = new();

        public void PlayAnimation(string clip, bool loop) =>
            Animations.Add(loop ? $"{clip}:loop" : clip);
    }

    private const uint MaguffinQuest = 1;
    private const uint MaguffinCube = 1;
    private const uint BountyQuest = 2;
    private const uint Keycard = 5;

    private static readonly Dictionary<string, DialogueGraph> Conversations = LoadConversations();

    private static Dictionary<string, DialogueGraph> LoadConversations()
    {
        var directory = DialogueDirectory();
        var loaded = new Dictionary<string, DialogueGraph>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(directory, "*" + YarnGraphCompiler.FileSuffix))
        {
            var problems = new List<string>();
            var id = YarnGraphCompiler.IdFromFileName(Path.GetFileName(path));
            loaded[id] = YarnGraphCompiler.Compile(id, File.ReadAllText(path), problems);
            Assert.Empty(problems.Select(p => $"{id}: {p}"));
        }
        return loaded;
    }

    private static string DialogueDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Resources", "Dialogue");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new DirectoryNotFoundException("Could not locate Resources/Dialogue above the test assembly.");
    }

    // Compiles a conversation the way an NPC role does, and fails on any runtime
    // warning (a dangling link or an unknown verb reports through the context).
    // A host is always supplied — the scene verbs warn without one, and a
    // conversation is free to gesture on any line.
    private static DialogueLine Play(string id, GameState state, FakeHost host = null, string speaker = "NPC")
    {
        var warnings = new List<string>();
        var context = new DialogueContext
        {
            State = state,
            SpeakerName = speaker,
            Host = host ?? new FakeHost(),
            LogWarning = warnings.Add,
        };
        var line = DialogueRuntime.Compile(Conversations[id], context);
        Assert.Empty(warnings.Select(w => $"{id}: {w}"));
        return line;
    }

    private static DialogueChoice Choice(DialogueLine line, string label)
    {
        var choice = line.Choices?.FirstOrDefault(c => c.Label == label);
        Assert.True(choice != null, $"no choice '{label}' (got: {Labels(line)})");
        return choice;
    }

    private static string Labels(DialogueLine line) =>
        line.Choices == null ? "none" : string.Join(", ", line.Choices.Select(c => $"'{c.Label}'"));

    // Walking a choice: its effects fire, then its target line shows.
    private static DialogueLine Pick(DialogueLine line, string label)
    {
        var choice = Choice(line, label);
        choice.Action?.Invoke();
        choice.Next?.OnShown?.Invoke();
        return choice.Next;
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

    private static GameState WithParty(int members)
    {
        var state = new GameState();
        for (var i = 0; i < members; i++)
        {
            state.Party.Add(Member((ulong)(i + 1)));
        }
        return state;
    }

    // --- Dockmaster Hale: the Maguffin fetch quest ---------------------------

    [Fact]
    public void HaleOffersTheQuestAndAcceptingStartsIt()
    {
        var state = new GameState();
        var line = Play("intro.dockmaster_hale", state);
        Assert.Contains("track it down", line.Text);

        var accepted = Pick(line, "I'll find it");
        Assert.Equal(QUESTSUCCESSSTATE.InProgress, state.GetQuestState(MaguffinQuest));
        Assert.Contains("Much obliged", accepted.Text);
    }

    [Fact]
    public void HaleDecliningLeavesTheQuestUnstarted()
    {
        var state = new GameState();
        var declined = Pick(Play("intro.dockmaster_hale", state), "Not now");
        Assert.Contains("won't walk back on its own", declined.Text);
        Assert.Equal(QUESTSUCCESSSTATE.Unstarted, state.GetQuestState(MaguffinQuest));
    }

    [Fact]
    public void HaleRemindsYouWhileTheQuestRunsAndTakesTheCubeOnTurnIn()
    {
        var state = new GameState();
        var host = new FakeHost();
        state.SetQuestState(MaguffinQuest, QUESTSUCCESSSTATE.InProgress);
        Assert.Contains("Any sign of my Maguffin Cube", Play("intro.dockmaster_hale", state, host).Text);

        state.Inventory.Add(MaguffinCube, 1);
        var turnin = Play("intro.dockmaster_hale", state, host);
        Assert.Contains("you found it", turnin.Text);

        var complete = Pick(turnin, "Hand it over");
        Assert.Contains("You have my thanks", complete.Text);
        Assert.Equal(QUESTSUCCESSSTATE.Success, state.GetQuestState(MaguffinQuest));
        Assert.Equal(0u, state.Inventory.CountOf(MaguffinCube));
        // He reaches out and takes it as the line shows.
        Assert.Equal(new[] { "PickUp" }, host.Animations);
    }

    [Fact]
    public void HaleTakesTheTurnInEvenIfTheCubeCameFirst()
    {
        // Picked the cube up before ever taking the quest: accepting the offer
        // goes straight to the turn-in rather than the "go find it" line.
        var state = new GameState();
        state.Inventory.Add(MaguffinCube, 1);
        var turnin = Pick(Play("intro.dockmaster_hale", state), "I'll find it");
        Assert.Contains("you found it", turnin.Text);
    }

    [Fact]
    public void RefusingHaleTwiceStartsAFight()
    {
        var state = new GameState();
        state.SetQuestState(MaguffinQuest, QUESTSUCCESSSTATE.InProgress);
        state.Inventory.Add(MaguffinCube, 1);
        var host = new FakeHost();

        var demand = Pick(Play("intro.dockmaster_hale", state, host), "I'm keeping it");
        Assert.Contains("station property", demand.Text);

        Pick(demand, "Try and take it");
        Assert.Equal(1, host.Battles);
        // Hale stays standing after the fight — only challengers despawn.
        Assert.False(host.LastBattleDespawned);
        // The cube is still yours, and the quest is untouched.
        Assert.Equal(1u, state.Inventory.CountOf(MaguffinCube));
        Assert.Equal(QUESTSUCCESSSTATE.InProgress, state.GetQuestState(MaguffinQuest));
    }

    [Fact]
    public void ABeatenHaleStopsPressingThePoint()
    {
        var state = new GameState();
        state.SetQuestState(MaguffinQuest, QUESTSUCCESSSTATE.InProgress);
        state.Inventory.Add(MaguffinCube, 1);
        state.MarkNpcDefeated("intro.dockmaster_hale");
        var host = new FakeHost();

        var gaveUp = Pick(Play("intro.dockmaster_hale", state, host), "I'm keeping it");
        Assert.Contains("not testing that arm twice", gaveUp.Text);
        Assert.Null(gaveUp.Choices);
        Assert.Equal(0, host.Battles);
    }

    [Fact]
    public void HaleThanksYouAfterTheQuestIsDone()
    {
        var state = new GameState();
        state.SetQuestState(MaguffinQuest, QUESTSUCCESSSTATE.Success);
        var line = Play("intro.dockmaster_hale", state);
        Assert.Contains("under lock and key", line.Text);
        Assert.Null(line.Choices);
    }

    // --- Chief Marlow: the Clear the Deck bounty ----------------------------

    [Fact]
    public void MarlowOffersTheBountyAndWaitsWhileVexStands()
    {
        var state = new GameState();
        var offer = Play("intro.chief_marlow", state);
        Assert.Contains("red-suited thug", offer.Text);

        Pick(offer, "I'll handle Vex");
        Assert.Equal(QUESTSUCCESSSTATE.InProgress, state.GetQuestState(BountyQuest));
        Assert.Contains("still strutting", Play("intro.chief_marlow", state).Text);
    }

    [Fact]
    public void MarlowPaysOutWhenVexIsDown()
    {
        var state = new GameState();
        var host = new FakeHost();
        state.SetQuestState(BountyQuest, QUESTSUCCESSSTATE.InProgress);
        state.MarkNpcDefeated("intro.vex");

        var turnin = Play("intro.chief_marlow", state, host);
        turnin.OnShown?.Invoke();
        Assert.Contains("deck finally goes quiet", turnin.Text);
        Assert.Equal(QUESTSUCCESSSTATE.Success, state.GetQuestState(BountyQuest));
        Assert.Equal(1u, state.Inventory.CountOf(Keycard));
        // He holds the keycard out as he says it.
        Assert.Equal(new[] { "Use_Item" }, host.Animations);
    }

    [Fact]
    public void MarlowStillPaysIfVexWasBeatenBeforeTheBounty()
    {
        var state = new GameState();
        state.MarkNpcDefeated("intro.vex");

        var offer = Play("intro.chief_marlow", state);
        Assert.Contains("about to post a bounty", offer.Text);

        var turnin = Pick(offer, "That was me");
        Assert.Contains("deck finally goes quiet", turnin.Text);
        Assert.Equal(QUESTSUCCESSSTATE.Success, state.GetQuestState(BountyQuest));
        Assert.Equal(1u, state.Inventory.CountOf(Keycard));
    }

    [Fact]
    public void MarlowSharesOneDeclineLineFromBothOffers()
    {
        foreach (var vexDown in new[] { false, true })
        {
            var state = new GameState();
            if (vexDown)
            {
                state.MarkNpcDefeated("intro.vex");
            }
            var declined = Pick(Play("intro.chief_marlow", state), "Not my problem");
            Assert.Contains("don't say I didn't warn you", declined.Text);
            Assert.Equal(QUESTSUCCESSSTATE.Unstarted, state.GetQuestState(BountyQuest));
        }
    }

    // --- Recruits ------------------------------------------------------------

    [Theory]
    [InlineData("intro.rig", 2ul)]
    [InlineData("intro.chief_marlow_recruit", 3ul)]
    public void ARecruitJoinsAsTheirOwnPartyMember(string id, ulong memberId)
    {
        var state = WithParty(1);
        var host = new FakeHost();

        var joined = Pick(Play(id, state, host), "Yes");
        Assert.Equal(new[] { memberId }, host.Recruited);
        Assert.Contains("Lead the way", joined.Text);
        Assert.Equal(new[] { "Cheering" }, host.Animations);
    }

    [Theory]
    [InlineData("intro.rig")]
    [InlineData("intro.chief_marlow_recruit")]
    public void ARecruitWavesYouOffWhenThePartyIsFull(string id)
    {
        var host = new FakeHost();
        var line = Play(id, WithParty(4), host);

        Assert.Contains("full crew", line.Text);
        Assert.Null(line.Choices);
        Assert.Empty(host.Recruited);
    }

    [Fact]
    public void DecliningARecruitLeavesThemOnTheDock()
    {
        var host = new FakeHost();
        var declined = Pick(Play("intro.rig", WithParty(1), host), "No");
        Assert.Contains("Suit yourself", declined.Text);
        Assert.Empty(host.Recruited);
    }

    // --- Vex and the shopkeeper ---------------------------------------------

    [Fact]
    public void VexFightsAndDespawnsWhenBeaten()
    {
        var host = new FakeHost();
        var greet = Play("intro.vex", new GameState(), host);
        Assert.Contains("my stretch of deck", greet.Text);

        // Blade out for the whole confrontation, not a one-shot flourish.
        greet.OnShown?.Invoke();
        Assert.Equal(new[] { "Sword_Idle:loop" }, host.Animations);

        Assert.Null(Pick(greet, "Settle it"));
        Assert.Equal(1, host.Battles);
        Assert.True(host.LastBattleDespawned);
    }

    [Fact]
    public void WalkingAwayFromVexEndsTheConversation()
    {
        var host = new FakeHost();
        var walk = Pick(Play("intro.vex", new GameState(), host), "Walk away");
        Assert.Contains("Keep walking", walk.Text);
        Assert.Equal(0, host.Battles);
    }

    [Fact]
    public void PayingVexOffMovesTheBountyWithoutAFight()
    {
        // The bounty's second route (quest-system.md Phase 4): Marlow wants the
        // plaza quiet and doesn't mind how, so buying Vex out reaches the same
        // beat beating him does.
        var state = new GameState { Credits = 250 };
        QuestManager.StartQuest(state, BountyQuest);
        var host = new FakeHost();

        var haggle = Pick(Play("intro.vex", state, host), "Marlow sent me. Name your price.");
        Assert.Contains("hundred and twenty", haggle.Text);

        var paid = Pick(haggle, "Pay him the 120");
        Assert.Contains("plaza's his", paid.Text);
        Assert.Equal(130u, state.Credits);
        Assert.True(state.IsFlagSet("vex_paid"));
        Assert.Equal(2u, state.GetQuestStage(BountyQuest));
        Assert.Equal(0, host.Battles);
    }

    [Fact]
    public void VexOnlyNamesAPriceYouCouldPay()
    {
        var state = new GameState { Credits = 100 };
        var greet = Play("intro.vex", state);
        Assert.DoesNotContain(greet.Choices, choice => choice.Label.StartsWith("Marlow sent me"));
    }

    [Fact]
    public void VexIsCordialOnceHeHasBeenPaid()
    {
        var state = new GameState();
        state.SetFlag("vex_paid", "true");

        var line = Play("intro.vex", state);

        Assert.Contains("We're square", line.Text);
        Assert.Null(line.Choices);
    }

    [Fact]
    public void MarlowPaysOutForTheBoughtOffRoute()
    {
        var state = new GameState();
        QuestManager.StartQuest(state, BountyQuest);
        state.SetFlag("vex_paid", "true");

        var turnin = Play("intro.chief_marlow", state);
        turnin.OnShown?.Invoke();

        Assert.Contains("Bought him off", turnin.Text);
        Assert.Equal(QUESTSUCCESSSTATE.Success, state.GetQuestState(BountyQuest));
        // The keycard is the quest's reward now, not a give_item in the line
        // above — which is what lets this route pay at all.
        Assert.Equal(1u, state.Inventory.CountOf(Keycard));
    }

    [Fact]
    public void PayingVexBeforeTheBountyStillCountsWhenMarlowAsks()
    {
        var state = new GameState { Credits = 250 };
        var paid = Pick(
            Pick(Play("intro.vex", state), "Marlow sent me. Name your price."),
            "Pay him the 120");
        Assert.Contains("somewhere warmer", paid.Text);
        // Nothing to move: the bounty hasn't been taken, and paying him off is
        // a perfectly good order to do this in.
        Assert.Equal(QUESTSUCCESSSTATE.Unstarted, state.GetQuestState(BountyQuest));

        var offer = Play("intro.chief_marlow", state);
        Assert.Contains("took his business two decks down", offer.Text);

        var turnin = Pick(offer, "Mine");
        Assert.Contains("Bought him off", turnin.Text);
        Assert.Equal(QUESTSUCCESSSTATE.Success, state.GetQuestState(BountyQuest));
        Assert.Equal(1u, state.Inventory.CountOf(Keycard));
    }

    [Fact]
    public void TheShopkeeperOpensTheShop()
    {
        var host = new FakeHost();
        var greet = Play("intro.shopkeeper", new GameState(), host);

        Assert.Null(Pick(greet, "Let's trade"));
        Assert.Equal(1, host.Shops);

        Assert.Contains("Mind the merchandise", Pick(greet, "Just looking").Text);
    }

    [Fact]
    public void TheShopkeeperGreetsYouDifferentlyTheSecondTime()
    {
        // The world-flag payoff (npc-dialogue-yarn.md Phase 3): the first visit
        // sets met_shopkeeper as the line shows, and every visit after that gets
        // the returning-customer greeting.
        var state = new GameState();
        var first = Play("intro.shopkeeper", state);
        Assert.Contains("Welcome in", first.Text);
        Assert.False(state.IsFlagSet("met_shopkeeper"));

        first.OnShown?.Invoke();
        Assert.True(state.IsFlagSet("met_shopkeeper"));

        var host = new FakeHost();
        var second = Play("intro.shopkeeper", state, host);
        Assert.Contains("Back again", second.Text);
        second.OnShown?.Invoke();
        Assert.Equal(new[] { "Waving" }, host.Animations);
        // Both greetings offer the same options and share the reply.
        Assert.Equal(
            first.Choices.Select(c => c.Label),
            second.Choices.Select(c => c.Label));
        Assert.Contains("Mind the merchandise", Pick(second, "Just looking").Text);
    }

    [Fact]
    public void MossOffersTheMedkitOnlyWhileYouCanAffordIt()
    {
        // dialogue-state-conditions Phase 3, both halves in one walk: the offer
        // is gated on the price before you spend, and the line after the payment
        // reads the balance it left behind. A fresh purse covers it exactly once.
        var state = new GameState { Credits = 250 };
        var greet = Play("intro.shopkeeper", state);
        greet.OnShown?.Invoke();

        var deal = Pick(greet, "Anything under the counter?");
        Assert.Contains("Hundred and fifty", deal.Text);

        var after = Pick(deal, "Done");
        Assert.Equal(100u, state.Credits);
        Assert.Equal(1u, state.Inventory.CountOf(ItemCatalog.MedkitId));
        Assert.Contains("Come back when your credits have recovered", after.Text);

        // And on the way back in, with 100 left, it isn't on the table at all.
        var next = Play("intro.shopkeeper", state);
        Assert.Contains("Back again", next.Text);
        Assert.DoesNotContain("Anything under the counter?", next.Choices.Select(c => c.Label));
    }

    [Fact]
    public void MossNoticesWhenYouCanStillAffordAnother()
    {
        // The other side of the same router: 400 in, 150 out, still a customer.
        var state = new GameState { Credits = 400 };
        var after = Pick(Pick(Play("intro.shopkeeper", state), "Anything under the counter?"), "Done");

        Assert.Equal(250u, state.Credits);
        Assert.Contains("if you're still buying", after.Text);
    }

    [Fact]
    public void TheShopkeeperStillRemembersYouAfterASaveAndReload()
    {
        // The other half of the Phase 3 acceptance: the flag is save state, not
        // session state.
        var root = Path.Combine(Path.GetTempPath(), $"spacerpg-intro-{Guid.NewGuid():N}");
        try
        {
            var state = new GameState { CurrentLevelPath = "res://Scenes/Levels/ShopInterior.tscn" };
            Play("intro.shopkeeper", state).OnShown?.Invoke();

            var repository = new SaveRepository(root);
            var slotId = Guid.NewGuid();
            repository.Save(
                new SaveData { SlotId = slotId, SaveVersion = SaveRepository.CurrentSaveVersion }, state);

            var reloaded = repository.LoadState(slotId);
            Assert.Contains("Back again", Play("intro.shopkeeper", reloaded).Text);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
