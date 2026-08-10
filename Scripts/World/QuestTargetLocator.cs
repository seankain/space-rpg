using Godot;

// Where a quest marker's target actually is (quest-markers.md Phase 2): the
// Godot half of the resolver's IQuestTargetLocator port, answering for the
// level that is running right now.
//
// Each target kind is answered by whatever owns its truth, and the live world
// outranks recorded data wherever it can:
//
//   * npc   — the spawned NPC's own position when it is in the tree (they
//             wander and patrol), else the spawn position on its
//             NpcDefinition, which is readable with every chunk unloaded.
//   * item  — the world Pickup when its chunk is streamed in (a pickup that
//             has been collected is simply gone, so the marker goes with it),
//             else the position recorded for it by the map bake.
//   * point — the coordinates the quest authored; nothing to look up.
//
// "Not found" is a normal answer — a target in another area, an un-baked
// chunk, an NPC that never spawned — and the resolver drops that marker.
public sealed class QuestTargetLocator : IQuestTargetLocator
{
    private readonly SceneTree tree;
    private readonly string areaName;
    private readonly string levelScenePath;

    public QuestTargetLocator(SceneTree tree, string areaName, string levelScenePath)
    {
        this.tree = tree;
        this.areaName = areaName ?? "";
        this.levelScenePath = levelScenePath ?? "";
    }

    // A locator for the level currently loaded, or null when nothing is
    // running. Interiors and unchunked scenes have no ChunkManager: they still
    // get a locator (live NPCs and pickups are findable) with no area name, so
    // baked lookups are skipped rather than pointed at the wrong area.
    public static QuestTargetLocator ForCurrentLevel()
    {
        var levelRoot = LevelManager.Instance?.LevelRoot;
        if (levelRoot == null || !levelRoot.IsInsideTree())
        {
            return null;
        }
        var chunkManager = ChunkManager.FindIn(levelRoot);
        return new QuestTargetLocator(
            levelRoot.GetTree(),
            chunkManager?.AreaName ?? "",
            chunkManager?.LevelScenePath() ?? levelRoot.SceneFilePath ?? "");
    }

    public bool TryLocate(QuestMarkerTarget target, out QuestTargetLocation location)
    {
        location = null;
        if (target == null)
        {
            return false;
        }
        location = target.Kind switch
        {
            QuestMarkerTargetKind.Npc => LocateNpc(target.Reference),
            QuestMarkerTargetKind.Item => LocateItem(target),
            _ => LocatePoint(target),
        };
        return location != null;
    }

    private QuestTargetLocation LocateNpc(string npcId)
    {
        foreach (var node in tree?.GetNodesInGroup(Npc.GroupName) ?? new Godot.Collections.Array<Node>())
        {
            if (node is Npc npc && npc.NpcId == npcId)
            {
                return At(npc.GlobalPosition, levelScenePath);
            }
        }
        var definition = NpcDatabase.Get(npcId);
        if (definition == null || string.IsNullOrEmpty(definition.SpawnScenePath))
        {
            return null;
        }
        return new QuestTargetLocation
        {
            // The definition knows which level it spawns into, but only the
            // running ChunkManager knows that level's area name — so an NPC in
            // another level is placed by scene path alone and left for the map
            // to route (or report) in Phase 4.
            AreaName = definition.SpawnScenePath == levelScenePath ? areaName : "",
            ScenePath = definition.SpawnScenePath,
            WorldX = MapProjection.ChunkToWorldX(definition.ChunkCoords.X, definition.LocalPosition.X),
            WorldZ = MapProjection.ChunkToWorldZ(definition.ChunkCoords.Y, definition.LocalPosition.Z),
        };
    }

    private QuestTargetLocation LocateItem(QuestMarkerTarget target)
    {
        if (!target.TryItemId(out var itemId))
        {
            return null;
        }
        foreach (var node in tree?.GetNodesInGroup(Pickup.GroupName) ?? new Godot.Collections.Array<Node>())
        {
            if (node is Pickup pickup && pickup.ItemId == itemId)
            {
                return At(pickup.GlobalPosition, levelScenePath);
            }
        }
        var baked = MapLandmarkCatalog.ForArea(areaName).FindPickup(itemId);
        return baked == null
            ? null
            : new QuestTargetLocation
            {
                AreaName = areaName,
                ScenePath = levelScenePath,
                WorldX = baked.X,
                WorldZ = baked.Z,
            };
    }

    // An authored point names its own area, so it resolves without asking
    // anything — including for an area that isn't the one running.
    private QuestTargetLocation LocatePoint(QuestMarkerTarget target) => new()
    {
        AreaName = target.Reference,
        ScenePath = target.Reference == areaName ? levelScenePath : "",
        WorldX = target.X,
        WorldZ = target.Z,
    };

    private QuestTargetLocation At(Vector3 position, string scenePath) => new()
    {
        AreaName = areaName,
        ScenePath = scenePath,
        WorldX = position.X,
        WorldZ = position.Z,
    };
}
