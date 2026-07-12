using System.Collections.Generic;
using Godot;

// Look of the generic battle arena a fight plays out on: a flat ground plane
// under a procedural skybox. Which theme is used depends on the world area
// the encounter started in (GameState.LocationName); real arena scenes with
// props can replace this table later without touching battle logic.
public class BattleArenaTheme
{
    public string Name { get; set; }
    public Color GroundColor { get; set; }
    public Color SkyTopColor { get; set; }
    public Color SkyHorizonColor { get; set; }
    public Color GroundHorizonColor { get; set; }
    public Color GroundBottomColor { get; set; }
    // Sun tint and strength for the arena's directional light.
    public Color SunColor { get; set; } = Colors.White;
    public float SunEnergy { get; set; } = 1.0f;

    private static readonly BattleArenaTheme StationDeck = new()
    {
        Name = "Station Deck",
        GroundColor = new Color(0.13f, 0.15f, 0.18f),
        SkyTopColor = new Color(0.02f, 0.03f, 0.08f),
        SkyHorizonColor = new Color(0.16f, 0.22f, 0.38f),
        GroundHorizonColor = new Color(0.16f, 0.22f, 0.38f),
        GroundBottomColor = new Color(0.05f, 0.06f, 0.1f),
        SunColor = new Color(0.85f, 0.9f, 1.0f),
        SunEnergy = 1.1f,
    };

    private static readonly BattleArenaTheme DesertWastes = new()
    {
        Name = "Desert Wastes",
        GroundColor = new Color(0.55f, 0.42f, 0.24f),
        SkyTopColor = new Color(0.45f, 0.28f, 0.12f),
        SkyHorizonColor = new Color(0.85f, 0.6f, 0.3f),
        GroundHorizonColor = new Color(0.85f, 0.6f, 0.3f),
        GroundBottomColor = new Color(0.3f, 0.2f, 0.1f),
        SunColor = new Color(1.0f, 0.85f, 0.6f),
        SunEnergy = 1.3f,
    };

    private static readonly BattleArenaTheme VerdantFields = new()
    {
        Name = "Verdant Fields",
        GroundColor = new Color(0.16f, 0.32f, 0.14f),
        SkyTopColor = new Color(0.25f, 0.45f, 0.75f),
        SkyHorizonColor = new Color(0.65f, 0.75f, 0.85f),
        GroundHorizonColor = new Color(0.65f, 0.75f, 0.85f),
        GroundBottomColor = new Color(0.12f, 0.18f, 0.12f),
        SunEnergy = 1.2f,
    };

    // World-area location name -> arena theme. Areas without an entry fall
    // back to the station deck (everything today is aboard stations anyway).
    private static readonly Dictionary<string, BattleArenaTheme> byLocation = new()
    {
        { "Intro Station", StationDeck },
        { "Desert Wastes", DesertWastes },
        { "Verdant Fields", VerdantFields },
    };

    public static BattleArenaTheme GetForLocation(string locationName) =>
        locationName != null && byLocation.TryGetValue(locationName, out var theme)
            ? theme
            : StationDeck;
}
