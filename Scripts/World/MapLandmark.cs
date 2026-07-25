using System.Collections.Generic;
using System.Text.Json;

// One scene-authored landmark on the world map: a portal or door found inside
// a chunk scene at bake time. Store landmarks are deliberately NOT in this
// file format — shopkeepers live in NpcDefinition data, which the map UI
// queries live through NpcDatabase, so moving an NPC never leaves a stale
// manifest.
//
// Engine-free on purpose (no Godot types): the xunit project compiles this
// file directly to round-trip the JSON without booting the engine.
public class MapLandmark
{
    public const string PortalType = "portal";
    public const string DoorType = "door";

    public string Type { get; set; } = "";

    // Display name shown on the map, e.g. Portal.TargetDisplayName.
    public string Name { get; set; } = "";

    // World-space position; MapProjection turns it into map pixels.
    public float X { get; set; }
    public float Z { get; set; }
}

// The per-area landmarks manifest, saved as
// Resources/Maps/<AreaName>/landmarks.json next to the baked chunk images.
public class MapLandmarksFile
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public int Version { get; set; } = CurrentVersion;

    public List<MapLandmark> Landmarks { get; set; } = new();

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static MapLandmarksFile FromJson(string json)
    {
        var file = JsonSerializer.Deserialize<MapLandmarksFile>(json, JsonOptions)
            ?? new MapLandmarksFile();
        file.Landmarks ??= new List<MapLandmark>();
        return file;
    }
}
