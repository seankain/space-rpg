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
    // Stable id of the world NPC this enemy embodies (NpcDefinition.NpcId).
    // The battle scene borrows that NPC's rigged CharacterMesh; empty for
    // enemies without a world NPC (drones, summoned mooks), which fall back
    // to the placeholder capsule.
    public string NpcId { get; set; } = "";
    // Placeholder capsule tint for enemies without a rigged mesh.
    public Color BodyColor { get; set; } = Colors.Red;
}

// The enemy side of one battle, plus the line shown as it starts.
public class BattleEncounter
{
    public string IntroMessage { get; set; }
    public List<EnemyDefinition> Enemies { get; set; } = new();
}

// Encounters keyed by the challenging NPC's stable id (NpcDefinition.NpcId,
// surfaced as Npc.NpcId) — an id-keyed registry like ItemCatalog, so
// renaming an NPC's DisplayName never detaches its fight. opponentName only
// labels the generic fallback for challengers without an authored encounter.
public static class EnemyCatalog
{
    public static BattleEncounter GetEncounter(string opponentId, string opponentName)
    {
        switch (opponentId)
        {
            case "intro.vex":
                return new BattleEncounter
                {
                    IntroMessage = "Vex draws his cutter!",
                    Enemies = new List<EnemyDefinition>
                    {
                        new EnemyDefinition
                        {
                            Name = "Vex",
                            NpcId = "intro.vex",
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
                opponentName = string.IsNullOrEmpty(opponentName) ? opponentId : opponentName;
                return new BattleEncounter
                {
                    IntroMessage = $"{opponentName} squares up!",
                    Enemies = new List<EnemyDefinition>
                    {
                        new EnemyDefinition
                        {
                            Name = opponentName,
                            // The challenger is an NPC, so their mesh still
                            // resolves even without an authored encounter.
                            NpcId = opponentId,
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
