using System.Collections.Generic;
using System.Linq;
using Xunit;

// NPC resource files plan Phase 1 acceptance: database indexing, duplicate
// detection, and item validation — exercised through NpcIndex, the
// engine-free core NpcDatabase feeds from .tres files.
public class NpcIndexTests
{
    private const string IntroScene = "res://Scenes/Levels/Intro.tscn";
    private const string ShopScene = "res://Scenes/Levels/ShopInterior.tscn";

    private sealed class FakeDefinition : INpcDefinition
    {
        public string NpcId { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string SpawnScenePath { get; init; } = IntroScene;
        public int ChunkX { get; init; }
        public int ChunkZ { get; init; }
        public List<uint> Items { get; init; } = new();
        public IEnumerable<uint> ItemIds => Items;
    }

    private readonly List<string> errors = new();

    private NpcIndex<FakeDefinition> NewIndex(params uint[] knownItems) =>
        new(knownItems.Contains, errors.Add);

    [Fact]
    public void AddedDefinitionIsFoundById()
    {
        var index = NewIndex();
        var vex = new FakeDefinition { NpcId = "intro.vex", DisplayName = "Vex" };
        Assert.True(index.TryAdd("intro.vex.tres", vex));
        Assert.Same(vex, index.Get("intro.vex"));
        Assert.Single(index.All);
        Assert.Empty(errors);
    }

    [Fact]
    public void DuplicateIdIsRejectedAndReported()
    {
        var index = NewIndex();
        Assert.True(index.TryAdd("a.tres", new FakeDefinition { NpcId = "intro.vex", DisplayName = "Vex" }));
        Assert.False(index.TryAdd("b.tres", new FakeDefinition { NpcId = "intro.vex", DisplayName = "Impostor" }));
        // The first definition wins; the duplicate is skipped entirely.
        Assert.Equal("Vex", index.Get("intro.vex").DisplayName);
        Assert.Single(index.All);
        Assert.Contains(errors, e => e.Contains("b.tres") && e.Contains("intro.vex"));
    }

    [Fact]
    public void EmptyIdIsRejected()
    {
        var index = NewIndex();
        Assert.False(index.TryAdd("nameless.tres", new FakeDefinition { NpcId = " " }));
        Assert.Empty(index.All);
        Assert.Single(errors);
    }

    [Fact]
    public void NullDefinitionIsRejected()
    {
        var index = NewIndex();
        Assert.False(index.TryAdd("not-an-npc.tres", null));
        Assert.Contains(errors, e => e.Contains("not-an-npc.tres"));
    }

    [Fact]
    public void MissingSpawnSceneIsRejected()
    {
        var index = NewIndex();
        Assert.False(index.TryAdd("homeless.tres",
            new FakeDefinition { NpcId = "intro.rig", SpawnScenePath = "" }));
        Assert.Empty(index.All);
        Assert.Single(errors);
    }

    [Fact]
    public void UnknownItemRejectsTheWholeDefinition()
    {
        var index = NewIndex(2);
        Assert.False(index.TryAdd("shopkeeper.tres",
            new FakeDefinition { NpcId = "intro.shopkeeper", Items = { 2, 999 } }));
        Assert.Null(index.Get("intro.shopkeeper"));
        Assert.Contains(errors, e => e.Contains("999"));
    }

    [Fact]
    public void KnownItemsAreAccepted()
    {
        var index = NewIndex(2, 3, 4);
        Assert.True(index.TryAdd("shopkeeper.tres",
            new FakeDefinition { NpcId = "intro.shopkeeper", Items = { 2, 3, 4 } }));
        Assert.Empty(errors);
    }

    [Fact]
    public void AtChunkFiltersBySceneAndCoordinates()
    {
        var index = NewIndex();
        index.TryAdd("vex.tres", new FakeDefinition { NpcId = "intro.vex", ChunkX = 0, ChunkZ = 0 });
        index.TryAdd("rig.tres", new FakeDefinition { NpcId = "intro.rig", ChunkX = 0, ChunkZ = 0 });
        index.TryAdd("far.tres", new FakeDefinition { NpcId = "intro.far", ChunkX = 1, ChunkZ = 0 });
        index.TryAdd("moss.tres", new FakeDefinition { NpcId = "intro.shopkeeper", SpawnScenePath = ShopScene });

        Assert.Equal(new[] { "intro.vex", "intro.rig" },
            index.AtChunk(IntroScene, 0, 0).Select(d => d.NpcId));
        Assert.Equal(new[] { "intro.far" },
            index.AtChunk(IntroScene, 1, 0).Select(d => d.NpcId));
        Assert.Empty(index.AtChunk(IntroScene, 5, 5));
        Assert.Empty(index.AtChunk("res://Scenes/Levels/Nowhere.tscn", 0, 0));
    }

    [Fact]
    public void InSceneReturnsEveryNpcRegardlessOfChunk()
    {
        var index = NewIndex();
        index.TryAdd("a.tres", new FakeDefinition { NpcId = "intro.a", ChunkX = 0, ChunkZ = 0 });
        index.TryAdd("b.tres", new FakeDefinition { NpcId = "intro.b", ChunkX = 3, ChunkZ = -1 });
        index.TryAdd("moss.tres", new FakeDefinition { NpcId = "intro.shopkeeper", SpawnScenePath = ShopScene, ChunkX = 7 });

        Assert.Equal(2, index.InScene(IntroScene).Count);
        Assert.Equal(new[] { "intro.shopkeeper" }, index.InScene(ShopScene).Select(d => d.NpcId));
        Assert.Empty(index.InScene(null));
    }

    [Fact]
    public void FindByDisplayNameResolvesLegacySaveEntries()
    {
        var index = NewIndex();
        index.TryAdd("vex.tres", new FakeDefinition { NpcId = "intro.vex", DisplayName = "Vex" });
        Assert.Equal("intro.vex", index.FindByDisplayName("Vex").NpcId);
        Assert.Null(index.FindByDisplayName("Nobody"));
        Assert.Null(index.FindByDisplayName(null));
    }
}
