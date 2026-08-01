using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

// The in-world NPC editor's engine-free core (in-game-editor plan Phase 7):
// the form model behind NpcEditorPanel — what makes an NPC savable, how the
// starting-inventory rows behave — and the generated ids the Place NPC tool
// hands out. The Godot panel and the .tres write are exercised in the running
// editor, the same split as EditorPlacement/PlacementMath.
public class NpcEditorTests
{
    private static NpcEditModel Valid() => new()
    {
        NpcId = "intro.newcomer",
        DisplayName = "Newcomer",
    };

    // Nothing is taken unless a test says so.
    private static readonly Func<string, bool> NoIdsTaken = _ => false;

    [Fact]
    public void AFilledInNpcHasNoProblems()
    {
        Assert.Empty(Valid().Validate(NoIdsTaken));
    }

    [Fact]
    public void DefaultsAreSavableAsSoonAsIdentityIsFilledIn()
    {
        // The Place NPC tool opens the form on a generated id and "New NPC",
        // so the defaults have to pass on their own — an author who only wants
        // a body standing there shouldn't have to fill in a character sheet.
        var model = new NpcEditModel { NpcId = "intro.npc1", DisplayName = "New NPC" };
        Assert.Empty(model.Validate(NoIdsTaken));
        Assert.Equal(1u, model.Level);
        Assert.True(model.MaxHealthPoints > 0);
        Assert.Equal(NpcEditModel.NoRig, model.RigName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyIdIsRejected(string id)
    {
        var model = Valid();
        model.NpcId = id;
        Assert.Contains(model.Validate(NoIdsTaken), p => p.Contains("id must not be empty"));
    }

    [Fact]
    public void AnIdWithSpacesIsRejectedBecauseItBecomesAFileName()
    {
        var model = Valid();
        model.NpcId = "intro new guy";
        Assert.Contains(model.Validate(NoIdsTaken), p => p.Contains("spaces"));
    }

    [Theory]
    [InlineData("intro/vex")]
    [InlineData("intro:vex")]
    [InlineData("intro*vex")]
    public void AnIdWithPathCharactersIsRejected(string id)
    {
        var model = Valid();
        model.NpcId = id;
        Assert.NotEmpty(model.Validate(NoIdsTaken));
    }

    [Fact]
    public void AnIdAlreadyInUseIsRejected()
    {
        var model = Valid();
        model.NpcId = "intro.vex";
        var problems = model.Validate(id => id == "intro.vex");
        Assert.Contains(problems, p => p.Contains("already exists"));
    }

    [Fact]
    public void AnExistingNpcKeepingItsOwnIdIsFine()
    {
        // The panel excludes the NPC being edited from the taken set, so
        // re-saving an NPC never trips the uniqueness check.
        var model = Valid();
        Assert.Empty(model.Validate(NoIdsTaken));
    }

    [Fact]
    public void AnEmptyDisplayNameIsRejected()
    {
        var model = Valid();
        model.DisplayName = "";
        Assert.Contains(model.Validate(NoIdsTaken), p => p.Contains("Display name"));
    }

    [Fact]
    public void ZeroMaxHealthIsRejected()
    {
        var model = Valid();
        model.MaxHealthPoints = 0;
        Assert.Contains(model.Validate(NoIdsTaken), p => p.Contains("Max health"));
    }

    [Fact]
    public void AnUnknownItemIdIsRejected()
    {
        var model = Valid();
        model.AddItem(9999, 1);
        Assert.Contains(model.Validate(NoIdsTaken), p => p.Contains("no item with id 9999"));
    }

    [Fact]
    public void AKnownItemIsAccepted()
    {
        var model = Valid();
        model.AddItem(ItemCatalog.MedkitId, 3);
        Assert.Empty(model.Validate(NoIdsTaken));
        Assert.Equal(3u, model.Items.Single().Quantity);
    }

    [Fact]
    public void AddingTheSameItemTwiceMergesItIntoOneRow()
    {
        var model = Valid();
        model.AddItem(ItemCatalog.MedkitId, 2);
        model.AddItem(ItemCatalog.MedkitId, 3);
        Assert.Single(model.Items);
        Assert.Equal(5u, model.Items[0].Quantity);
    }

    [Fact]
    public void AddingZeroOfSomethingStillAddsOne()
    {
        // The "+ item" button passes 1, but a row that landed at zero would be
        // a save-blocking problem the author never asked for.
        var model = Valid();
        model.AddItem(ItemCatalog.MedkitId, 0);
        Assert.Equal(1u, model.Items[0].Quantity);
    }

    [Fact]
    public void AQuantityEditedToZeroIsRejected()
    {
        var model = Valid();
        model.AddItem(ItemCatalog.MedkitId, 1);
        model.Items[0].Quantity = 0;
        Assert.Contains(model.Validate(NoIdsTaken), p => p.Contains("quantity must be at least 1"));
    }

    [Fact]
    public void RemovingARowDropsIt()
    {
        var model = Valid();
        model.AddItem(ItemCatalog.MedkitId, 1);
        model.AddItem(ItemCatalog.PlasmaCutterId, 1);
        model.RemoveItem(0);
        Assert.Equal(ItemCatalog.PlasmaCutterId, model.Items.Single().ItemId);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void RemovingAnOutOfRangeRowIsHarmless(int index)
    {
        var model = Valid();
        model.AddItem(ItemCatalog.MedkitId, 1);
        model.RemoveItem(index);
        Assert.Single(model.Items);
    }

    [Fact]
    public void CopyStatsHandsOutADetachedBlock()
    {
        var model = Valid();
        model.Stats.Strength = 12;
        var copy = model.CopyStats();
        copy.Strength = 3;
        Assert.Equal(12u, model.Stats.Strength);
    }

    [Fact]
    public void EveryProblemIsReportedAtOnce()
    {
        // The panel lists them, so validation must not stop at the first.
        var model = new NpcEditModel { NpcId = "", DisplayName = "", MaxHealthPoints = 0 };
        Assert.True(model.Validate(NoIdsTaken).Count >= 3);
    }

    // --- generated ids (the Place NPC tool) ----------------------------------

    [Fact]
    public void APlacedNpcGetsTheFirstFreeNumberedIdForItsArea()
    {
        Assert.Equal("intro.npc1", PlacementMath.NextNpcId("Intro", Array.Empty<string>()));
        Assert.Equal("intro.npc2", PlacementMath.NextNpcId("Intro", new[] { "intro.npc1" }));
        // Gaps are reused — deleting npc1 hands its number back.
        Assert.Equal("intro.npc1", PlacementMath.NextNpcId("Intro", new[] { "intro.npc2" }));
    }

    [Fact]
    public void GeneratedIdsIgnoreCaseWhenCheckingWhatIsTaken()
    {
        Assert.Equal("intro.npc2", PlacementMath.NextNpcId("Intro", new[] { "INTRO.NPC1" }));
    }

    [Fact]
    public void GeneratedIdsAreLowerCasedAndAreaScoped()
    {
        Assert.Equal("shopinterior.npc1", PlacementMath.NextNpcId("ShopInterior", new List<string>()));
    }

    [Fact]
    public void AnAreaLessLevelStillGetsAUsableId()
    {
        Assert.Equal("npc1", PlacementMath.NextNpcId("", null));
    }

    [Fact]
    public void AGeneratedIdIsAlwaysAValidNpcId()
    {
        var id = PlacementMath.NextNpcId("Intro", new[] { "intro.npc1", "intro.npc2" });
        var model = new NpcEditModel { NpcId = id, DisplayName = "New NPC" };
        Assert.Empty(model.Validate(taken => taken is "intro.npc1" or "intro.npc2"));
    }
}
