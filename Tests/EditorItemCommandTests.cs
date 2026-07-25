using System.Collections.Generic;
using Xunit;

// In-game-editor plan Phase 4 acceptance: the engine-free item/credit commands
// run against a fabricated GameState — correct stacks, cap splitting, unknown-id
// rejection, and credit set/add/subtract. The Godot wrappers (which only fetch
// SaveManager.Instance.CurrentState) are exercised in the running editor.
public class EditorItemCommandTests
{
    private static GameState NewState() => new();

    private static IReadOnlyList<string> Args(params string[] a) => a;

    [Fact]
    public void GiveAddsItemByIdWithQuantity()
    {
        var state = NewState();
        var result = EditorItemCommands.Give(state, Args(ItemCatalog.MedkitId.ToString(), "2"));
        Assert.True(result.Success);
        Assert.Equal(2u, state.Inventory.CountOf(ItemCatalog.MedkitId));
    }

    [Fact]
    public void GiveDefaultsToOne()
    {
        var state = NewState();
        EditorItemCommands.Give(state, Args(ItemCatalog.MedkitId.ToString()));
        Assert.Equal(1u, state.Inventory.CountOf(ItemCatalog.MedkitId));
    }

    [Fact]
    public void GiveRejectsUnknownId()
    {
        var state = NewState();
        var result = EditorItemCommands.Give(state, Args("99999"));
        Assert.False(result.Success);
        Assert.Empty(state.Inventory.Stacks);
    }

    [Fact]
    public void GiveRejectsZeroQuantity()
    {
        var state = NewState();
        var result = EditorItemCommands.Give(state, Args(ItemCatalog.MedkitId.ToString(), "0"));
        Assert.False(result.Success);
        Assert.Empty(state.Inventory.Stacks);
    }

    [Fact]
    public void GiveSplitsAcrossStackCaps()
    {
        var state = NewState();
        // The Maguffin Cube is a quest item (MaxStackSize 1), so three of them
        // land in three separate stacks.
        var result = EditorItemCommands.Give(state, Args(ItemCatalog.MaguffinCubeId.ToString(), "3"));
        Assert.True(result.Success);
        Assert.Equal(3u, state.Inventory.CountOf(ItemCatalog.MaguffinCubeId));
        Assert.Equal(3, state.Inventory.Stacks.Count);
    }

    [Fact]
    public void GiveByNameResolvesCatalogName()
    {
        var state = NewState();
        var result = EditorItemCommands.GiveByName(state, Args("Medkit", "2"));
        Assert.True(result.Success);
        Assert.Equal(2u, state.Inventory.CountOf(ItemCatalog.MedkitId));
    }

    [Fact]
    public void GiveByNameRejectsUnknownName()
    {
        var state = NewState();
        Assert.False(EditorItemCommands.GiveByName(state, Args("Nonexistent Widget")).Success);
    }

    [Fact]
    public void TakeItemRemovesUpToHeld()
    {
        var state = NewState();
        EditorItemCommands.Give(state, Args(ItemCatalog.MedkitId.ToString(), "5"));

        var partial = EditorItemCommands.Take(state, Args(ItemCatalog.MedkitId.ToString(), "2"));
        Assert.True(partial.Success);
        Assert.Equal(3u, state.Inventory.CountOf(ItemCatalog.MedkitId));

        // Removing more than held changes nothing and reports failure.
        var tooMany = EditorItemCommands.Take(state, Args(ItemCatalog.MedkitId.ToString(), "10"));
        Assert.False(tooMany.Success);
        Assert.Equal(3u, state.Inventory.CountOf(ItemCatalog.MedkitId));
    }

    [Fact]
    public void TakeItemRejectsUnknownId()
    {
        Assert.False(EditorItemCommands.Take(NewState(), Args("99999")).Success);
    }

    [Fact]
    public void CreditsSetsAddsAndSubtracts()
    {
        var state = NewState();

        EditorItemCommands.Credits(state, Args("1000"));
        Assert.Equal(1000u, state.Credits);

        EditorItemCommands.Credits(state, Args("+500"));
        Assert.Equal(1500u, state.Credits);

        // Subtracting past zero clamps rather than underflowing the uint.
        EditorItemCommands.Credits(state, Args("-2000"));
        Assert.Equal(0u, state.Credits);
    }

    [Fact]
    public void CreditsRejectsBadAmount()
    {
        var state = NewState();
        var before = state.Credits;
        Assert.False(EditorItemCommands.Credits(state, Args("lots")).Success);
        Assert.Equal(before, state.Credits);
    }

    [Fact]
    public void CommandsRejectMissingGameState()
    {
        Assert.False(EditorItemCommands.Give(null, Args(ItemCatalog.MedkitId.ToString())).Success);
        Assert.False(EditorItemCommands.Take(null, Args(ItemCatalog.MedkitId.ToString())).Success);
        Assert.False(EditorItemCommands.Credits(null, Args("100")).Success);
        Assert.False(EditorItemCommands.GiveByName(null, Args("Medkit")).Success);
    }
}
