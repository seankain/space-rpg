using System;
using System.Collections.Generic;

// Where the tracked quest's markers go on the map that is currently open
// (quest-markers.md Phase 4). Three outcomes, and the second is the one that
// makes the feature useful in a world with interiors:
//
//   * OnMap      — the target is in this area; draw it at its world position.
//   * AtEntrance — the target is inside a level this map doesn't cover, and a
//                  door or portal here leads to it: draw the marker on that
//                  door. "Go through this door" is the actionable instruction.
//   * Elsewhere  — nothing on this map points at it. No icon; the map names it
//                  in the header instead of silently showing one fewer
//                  objective.
//
// Engine-free (the drawing is MapMenu's job), so the routing rule is unit-
// tested rather than eyeballed at four zoom levels.
public static class QuestMarkerPlacements
{
    // Every active marker of the tracked quest, each classified against the
    // area the map is showing. Empty when nothing is tracked.
    public static IReadOnlyList<QuestMarkerPlacement> ForTrackedQuest(
        GameState state,
        IQuestTargetLocator locator,
        string areaName,
        string levelScenePath,
        MapLandmarksFile landmarks,
        Action<string> warn = null)
    {
        var placements = new List<QuestMarkerPlacement>();
        var quest = state == null ? null : QuestCatalog.Get(state.TrackedQuestId);
        if (quest == null)
        {
            return placements;
        }
        foreach (var marker in QuestMarkerResolver.ActiveMarkers(state, quest, warn))
        {
            QuestTargetLocation location = null;
            locator?.TryLocate(marker.Target, out location);
            placements.Add(For(quest, marker, location, areaName, levelScenePath, landmarks));
        }
        return placements;
    }

    public static QuestMarkerPlacement For(
        Quest quest,
        QuestMarker marker,
        QuestTargetLocation location,
        string areaName,
        string levelScenePath,
        MapLandmarksFile landmarks)
    {
        var placement = new QuestMarkerPlacement
        {
            QuestId = quest?.Id ?? 0,
            SideQuest = quest?.SideQuest ?? false,
            Label = marker?.Label ?? "",
        };
        if (location == null)
        {
            // The locator couldn't place it at all: an NPC that never spawned,
            // an item whose chunk has never been baked.
            placement.Kind = QuestMarkerPlacementKind.Elsewhere;
            return placement;
        }
        if (IsHere(location, areaName, levelScenePath))
        {
            placement.Kind = QuestMarkerPlacementKind.OnMap;
            placement.WorldX = location.WorldX;
            placement.WorldZ = location.WorldZ;
            return placement;
        }
        if (landmarks?.FindEntranceTo(location.ScenePath) is { } entrance)
        {
            placement.Kind = QuestMarkerPlacementKind.AtEntrance;
            placement.WorldX = entrance.X;
            placement.WorldZ = entrance.Z;
            placement.Where = entrance.Name;
            return placement;
        }
        placement.Kind = QuestMarkerPlacementKind.Elsewhere;
        placement.Where = !string.IsNullOrEmpty(location.AreaName)
            ? location.AreaName
            : SceneName(location.ScenePath);
        return placement;
    }

    // The target belongs to the area on screen. Either name matching is enough:
    // a live position carries the running area, while a position read from an
    // NpcDefinition only knows the level scene it spawns into.
    private static bool IsHere(QuestTargetLocation location, string areaName, string levelScenePath) =>
        (!string.IsNullOrEmpty(areaName) && location.AreaName == areaName)
        || (!string.IsNullOrEmpty(levelScenePath) && location.ScenePath == levelScenePath);

    // "res://Scenes/Levels/ShopInterior.tscn" -> "ShopInterior": the least-bad
    // name for a place the player has no other name for yet.
    public static string SceneName(string scenePath)
    {
        if (string.IsNullOrEmpty(scenePath))
        {
            return "";
        }
        var start = scenePath.LastIndexOf('/') + 1;
        var end = scenePath.LastIndexOf('.');
        return end > start ? scenePath[start..end] : scenePath[start..];
    }
}

public enum QuestMarkerPlacementKind
{
    OnMap,
    AtEntrance,
    Elsewhere,
}

public class QuestMarkerPlacement
{
    public uint QuestId { get; set; }

    // Carried from the quest so the map can colour main and side objectives
    // differently without a catalog lookup per icon.
    public bool SideQuest { get; set; }

    // The objective text the marker was authored with.
    public string Label { get; set; } = "";

    public QuestMarkerPlacementKind Kind { get; set; }

    // Map position, for OnMap and AtEntrance.
    public float WorldX { get; set; }
    public float WorldZ { get; set; }

    // The entrance's name (AtEntrance) or the place the target is in
    // (Elsewhere); empty when even that is unknown.
    public string Where { get; set; } = "";

    public bool IsDrawn => Kind is QuestMarkerPlacementKind.OnMap or QuestMarkerPlacementKind.AtEntrance;

    // What to call this objective on screen: the authored label, plus where it
    // actually is when that isn't the spot being pointed at.
    public string DisplayText => Kind switch
    {
        QuestMarkerPlacementKind.AtEntrance when !string.IsNullOrEmpty(Where) => $"{Label} — inside {Where}",
        QuestMarkerPlacementKind.Elsewhere when !string.IsNullOrEmpty(Where) => $"{Label} — in {Where}",
        QuestMarkerPlacementKind.Elsewhere => $"{Label} — not on this map",
        _ => Label,
    };
}
