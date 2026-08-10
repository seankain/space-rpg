using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

// One scene-authored thing the map cares about, found inside a chunk scene at
// bake time: a portal, a door, or a world pickup. Store landmarks are
// deliberately NOT in this file format — shopkeepers live in NpcDefinition
// data, which the map UI queries live through NpcDatabase, so moving an NPC
// never leaves a stale manifest.
//
// Not everything in here is drawn. Pickups are recorded for quest markers
// (quest-markers.md Phase 2) — "the Maguffin Cube is over there" needs a
// position for a chunk that is usually streamed out — but the map only draws
// portals and doors, so a manifest entry is a fact about the world rather than
// an icon by itself.
//
// Engine-free on purpose (no Godot types): the xunit project compiles this
// file directly to round-trip the JSON without booting the engine.
public class MapLandmark
{
    public const string PortalType = "portal";
    public const string DoorType = "door";
    public const string PickupType = "pickup";

    public string Type { get; set; } = "";

    // Display name shown on the map, e.g. Portal.TargetDisplayName, or the
    // item's catalog name for a pickup.
    public string Name { get; set; } = "";

    // World-space position; MapProjection turns it into map pixels.
    public float X { get; set; }
    public float Z { get; set; }

    // Pickups only: the ItemCatalog id lying there, so an item-target quest
    // marker can find it. 0 for every other type.
    public uint ItemId { get; set; }

    // Doors and portals only: the level scene they lead to. A quest marker
    // whose target lives in that scene routes to this landmark instead — "go
    // through this door" is the actionable instruction when the objective is
    // inside an interior the current map doesn't cover.
    public string TargetScenePath { get; set; } = "";
}

// The per-area landmarks manifest, saved as
// Resources/Maps/<AreaName>/landmarks.json next to the baked chunk images.
public class MapLandmarksFile
{
    // v2: pickup entries, plus ItemId/TargetScenePath on a landmark
    // (quest-markers.md Phase 2). A v1 file still loads — it simply has no
    // pickups, so item-target markers find nothing until the area is re-baked.
    public const int CurrentVersion = 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public int Version { get; set; } = CurrentVersion;

    public List<MapLandmark> Landmarks { get; set; } = new();

    public IEnumerable<MapLandmark> OfType(string type) =>
        Landmarks.Where(landmark => landmark.Type == type);

    // Where the world pickup for an item was standing when the area was baked.
    // Null when the area has no such pickup, or was baked before pickups were
    // recorded (a v1 manifest).
    public MapLandmark FindPickup(uint itemId) =>
        OfType(MapLandmark.PickupType).FirstOrDefault(pickup => pickup.ItemId == itemId);

    // The door or portal leading into a level, for routing a marker whose
    // target lives somewhere this map doesn't cover.
    public MapLandmark FindEntranceTo(string scenePath) =>
        string.IsNullOrEmpty(scenePath)
            ? null
            : Landmarks.FirstOrDefault(landmark =>
                landmark.TargetScenePath == scenePath
                && landmark.Type is MapLandmark.DoorType or MapLandmark.PortalType);

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static MapLandmarksFile FromJson(string json)
    {
        var file = JsonSerializer.Deserialize<MapLandmarksFile>(json, JsonOptions)
            ?? new MapLandmarksFile();
        file.Landmarks ??= new List<MapLandmark>();
        return file;
    }
}
