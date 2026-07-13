using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Xunit;

// Party plan Phase 1 acceptance: a multi-member party round-trips through
// save/load, including the per-member positions Phase 2's followers persist.
public class PartySaveRoundTripTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"spacerpg-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MultiMemberPartyRoundTrips()
    {
        var repository = new SaveRepository(root);
        var slotId = Guid.NewGuid();
        var state = new GameState
        {
            CurrentLevelPath = "res://Scenes/Levels/Intro.tscn",
            LocationName = "Intro Station",
            LocationId = Guid.NewGuid(),
            Party = new List<CharacterEntity>
            {
                new()
                {
                    Id = 1,
                    Name = "Captain",
                    Level = 3,
                    ExperiencePoints = 120,
                    HealthPoints = 7,
                    MaxHealthPoints = 12,
                    MagicPoints = 2,
                    MaxMagicPoints = 6,
                    Position = new Vector3(4.5f, 0f, -2.25f),
                    Stats = new CharacterStats { Strength = 8, Dexterity = 6 },
                    EquipSlots = new CharacterEquipSlots { ChestItemId = 4 },
                    ActiveStatusEffects = new List<ActiveStatusEffect>(),
                },
                new()
                {
                    Id = 2,
                    Name = "Rig",
                    Level = 1,
                    HealthPoints = 8,
                    MaxHealthPoints = 8,
                    MagicPoints = 3,
                    MaxMagicPoints = 3,
                    Position = new Vector3(2.0f, 0f, -0.5f),
                    Stats = new CharacterStats { Dexterity = 7 },
                    EquipSlots = new CharacterEquipSlots(),
                    ActiveStatusEffects = new List<ActiveStatusEffect>(),
                },
            },
        };

        repository.Save(new SaveData { SlotId = slotId, SaveVersion = SaveRepository.CurrentSaveVersion }, state);
        var loaded = repository.LoadState(slotId);

        Assert.Equal(2, loaded.Party.Count);
        // Order is meaningful: index 0 is the leader.
        Assert.Equal("Captain", loaded.Party[0].Name);
        Assert.Equal("Rig", loaded.Party[1].Name);
        Assert.Equal(2ul, loaded.Party[1].Id);
        Assert.Equal(7u, loaded.Party[0].HealthPoints);
        Assert.Equal(12u, loaded.Party[0].MaxHealthPoints);
        Assert.Equal(120u, loaded.Party[0].ExperiencePoints);
        Assert.Equal(8u, loaded.Party[0].Stats.Strength);
        Assert.Equal(7u, loaded.Party[1].Stats.Dexterity);
        Assert.Equal(4u, loaded.Party[0].EquipSlots.ChestItemId);
        Assert.Equal(new Vector3(4.5f, 0f, -2.25f), loaded.Party[0].Position);
        Assert.Equal(new Vector3(2.0f, 0f, -0.5f), loaded.Party[1].Position);
    }

    [Fact]
    public void PartylessSaveLoadsWithEmptyRoster()
    {
        var repository = new SaveRepository(root);
        var slotId = Guid.NewGuid();
        repository.Save(
            new SaveData { SlotId = slotId, SaveVersion = 1 },
            new GameState { CurrentLevelPath = "res://Scenes/Levels/Intro.tscn", Party = null });

        var loaded = repository.LoadState(slotId);

        Assert.NotNull(loaded.Party);
        Assert.Empty(loaded.Party);
    }
}
