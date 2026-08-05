using Godot;
using System.Collections.Generic;

// The baked landmarks manifests, cached per area — the ItemCatalog/NpcDatabase
// pattern applied to Resources/Maps/<Area>/landmarks.json. Two readers now
// (the Map tab draws portals and doors from it, and the quest-marker locator
// looks up pickups in it), so parsing moved out of MapMenu rather than being
// written twice.
//
// A missing manifest is an area nobody has baked yet, not an error: it reads as
// an empty file, and everything downstream simply has nothing to show. Godot's
// FileAccess (not System.IO) so this works against res:// in an exported build.
public static class MapLandmarkCatalog
{
    public const string MapsRoot = "res://Resources/Maps";

    private static readonly Dictionary<string, MapLandmarksFile> cache = new();

    public static MapLandmarksFile ForArea(string areaName)
    {
        if (string.IsNullOrEmpty(areaName))
        {
            return new MapLandmarksFile();
        }
        if (cache.TryGetValue(areaName, out var cached))
        {
            return cached;
        }
        var loaded = Load(areaName);
        cache[areaName] = loaded;
        return loaded;
    }

    // Drop the cache after a re-bake, so a running game picks up a fresh
    // manifest the way NpcDatabase.Invalidate does for edited NPCs.
    public static void Invalidate() => cache.Clear();

    private static MapLandmarksFile Load(string areaName)
    {
        var path = $"{MapsRoot}/{areaName}/landmarks.json";
        if (!FileAccess.FileExists(path))
        {
            return new MapLandmarksFile();
        }
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PushWarning($"MapLandmarkCatalog: could not read '{path}': {FileAccess.GetOpenError()}");
            return new MapLandmarksFile();
        }
        return MapLandmarksFile.FromJson(file.GetAsText());
    }
}
