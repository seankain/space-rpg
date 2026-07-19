using System.Collections.Generic;

// What a power does to its target; also decides which side gets targeted
// (damage powers aim at enemies, restorative powers at allies).
public enum PowerEffect
{
    Damage,
    Heal,
}

// A battle power (spell/ability) that spends power points (the MagicPoints
// field on CharacterEntity). Definitions live in PowerCatalog and are
// referenced by id, mirroring ItemCatalog/QuestCatalog.
public class Power
{
    public uint Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public uint PowerPointCost { get; set; }
    public PowerEffect Effect { get; set; }
    // Base magnitude before stat scaling (Intelligence for damage, Wisdom
    // for healing; see BattleScene damage math).
    public uint Amount { get; set; }
    // Effect played on the target when the power lands (VfxLibrary.Spawn).
    public VfxId Vfx { get; set; }
}

// All power definitions, keyed by Power.Id. Same registry pattern as
// ItemCatalog: ids are permanent once shipped — never reuse them.
public static class PowerCatalog
{
    public const uint PlasmaSurgeId = 1;
    public const uint NanoMendId = 2;

    private static readonly Dictionary<uint, Power> powers = new();

    static PowerCatalog()
    {
        Register(new Power
        {
            Id = PlasmaSurgeId,
            Name = "Plasma Surge",
            Description = "Vent superheated plasma at one enemy.",
            PowerPointCost = 2,
            Effect = PowerEffect.Damage,
            Amount = 4,
            Vfx = VfxId.PlasmaBurst,
        });
        Register(new Power
        {
            Id = NanoMendId,
            Name = "Nano Mend",
            Description = "A swarm of repair nanites knits one ally's wounds.",
            PowerPointCost = 2,
            Effect = PowerEffect.Heal,
            Amount = 5,
            Vfx = VfxId.NanoMend,
        });
    }

    private static void Register(Power power)
    {
        powers.Add(power.Id, power);
    }

    public static Power Get(uint id) => powers.TryGetValue(id, out var power) ? power : null;

    public static IReadOnlyCollection<Power> All => powers.Values;
}
