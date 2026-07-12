using System;
using System.Collections.Generic;
using System.Linq;

// Live state of one fighter for the duration of a battle. Party members wrap
// their persistent CharacterEntity (results are written back on victory);
// enemies are built fresh from an EnemyDefinition and discarded afterwards.
public class BattleCombatant
{
    public string Name { get; set; }
    public bool IsPartyMember { get; set; }
    // Set for party members only.
    public CharacterEntity Entity { get; set; }
    public uint HealthPoints { get; set; }
    public uint MaxHealthPoints { get; set; }
    public uint PowerPoints { get; set; }
    public uint MaxPowerPoints { get; set; }
    public CharacterStats Stats { get; set; }
    public List<Power> Powers { get; set; } = new();
    public uint XpReward { get; set; }

    public bool IsDown => HealthPoints == 0;

    public void TakeDamage(uint amount) =>
        HealthPoints = amount >= HealthPoints ? 0 : HealthPoints - amount;

    public void Heal(uint amount) =>
        HealthPoints = Math.Min(MaxHealthPoints, HealthPoints + amount);

    public static BattleCombatant FromPartyMember(CharacterEntity entity) => new()
    {
        Name = entity.Name,
        IsPartyMember = true,
        Entity = entity,
        HealthPoints = entity.HealthPoints,
        MaxHealthPoints = entity.MaxHealthPoints,
        PowerPoints = entity.MagicPoints,
        MaxPowerPoints = entity.MaxMagicPoints,
        Stats = entity.Stats ?? new CharacterStats(),
        // Every party member knows the whole catalog for now; per-character
        // learned powers come with the progression system.
        Powers = PowerCatalog.All.ToList(),
    };

    public static BattleCombatant FromEnemyDefinition(EnemyDefinition definition) => new()
    {
        Name = definition.Name,
        IsPartyMember = false,
        HealthPoints = definition.MaxHealthPoints,
        MaxHealthPoints = definition.MaxHealthPoints,
        PowerPoints = definition.MaxPowerPoints,
        MaxPowerPoints = definition.MaxPowerPoints,
        Stats = definition.Stats ?? new CharacterStats(),
        Powers = definition.PowerIds.Select(PowerCatalog.Get).Where(p => p != null).ToList(),
        XpReward = definition.XpReward,
    };
}

public enum BattleActionKind
{
    Attack,
    Power,
    Item,
}

// One resolved choice for one turn: who does what to whom. Built either by
// the HUD menus (party) or enemy AI, then executed by BattleScene.
public class BattleAction
{
    public BattleActionKind Kind { get; set; }
    public BattleCombatant Actor { get; set; }
    public BattleCombatant Target { get; set; }
    // Set when Kind == Power.
    public Power Power { get; set; }
    // Set when Kind == Item; references ItemCatalog.
    public uint ItemId { get; set; }
}
