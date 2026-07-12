using System.Collections.Generic;
using Godot;

// Static definition of one enemy type. Instantiated into a BattleCombatant
// per encounter, so the same definition can appear multiple times.
public class EnemyDefinition
{
    public string Name { get; set; }
    public uint MaxHealthPoints { get; set; }
    public uint MaxPowerPoints { get; set; }
    public CharacterStats Stats { get; set; } = new();
    public List<uint> PowerIds { get; set; } = new();
    public uint XpReward { get; set; }
    // Placeholder capsule tint until enemies get real models.
    public Color BodyColor { get; set; } = Colors.Red;
}

// The enemy side of one battle, plus the line shown as it starts.
public class BattleEncounter
{
    public string IntroMessage { get; set; }
    public List<EnemyDefinition> Enemies { get; set; } = new();
}

// Encounters keyed by the challenging NPC's display name — the only handle
// BattleNpc dialogue has today. Once field NPCs carry real encounter ids
// this becomes an id-keyed registry like ItemCatalog.
public static class EnemyCatalog
{
    public static BattleEncounter GetEncounter(string opponentName)
    {
        switch (opponentName)
        {
            case "Vex":
                return new BattleEncounter
                {
                    IntroMessage = "Vex draws his cutter!",
                    Enemies = new List<EnemyDefinition>
                    {
                        new EnemyDefinition
                        {
                            Name = "Vex",
                            MaxHealthPoints = 12,
                            MaxPowerPoints = 4,
                            Stats = new CharacterStats
                            {
                                Strength = 6, Intelligence = 5, Constitution = 5,
                                Dexterity = 6, Wisdom = 3, Charisma = 4,
                            },
                            PowerIds = new List<uint> { PowerCatalog.PlasmaSurgeId },
                            XpReward = 12,
                            BodyColor = new Color(0.9f, 0.2f, 0.2f),
                        },
                        new EnemyDefinition
                        {
                            Name = "Dock Drone",
                            MaxHealthPoints = 6,
                            Stats = new CharacterStats
                            {
                                Strength = 4, Intelligence = 2, Constitution = 3,
                                Dexterity = 8, Wisdom = 2, Charisma = 1,
                            },
                            XpReward = 5,
                            BodyColor = new Color(0.55f, 0.55f, 0.6f),
                        },
                    },
                };
            default:
                // Unknown challengers still produce a playable fight instead
                // of a crash — a lone generic brawler wearing their name.
                return new BattleEncounter
                {
                    IntroMessage = $"{opponentName} squares up!",
                    Enemies = new List<EnemyDefinition>
                    {
                        new EnemyDefinition
                        {
                            Name = opponentName,
                            MaxHealthPoints = 10,
                            Stats = new CharacterStats
                            {
                                Strength = 5, Intelligence = 3, Constitution = 4,
                                Dexterity = 5, Wisdom = 3, Charisma = 3,
                            },
                            XpReward = 8,
                        },
                    },
                };
        }
    }
}
