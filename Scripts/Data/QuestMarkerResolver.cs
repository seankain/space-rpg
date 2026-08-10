using System;
using System.Collections.Generic;

// Turns a quest's authored markers into "here, on this map" (quest-markers.md
// Phase 1). Two steps, deliberately separable:
//
//   * ActiveMarkers — which markers apply right now, from quest progress and
//     each marker's condition. Labels come out of here, so the journal can
//     list objectives it has no position for.
//   * Resolve — those markers with a world position attached, for the map.
//
// Finding a position is a port (IQuestTargetLocator), not a dependency: NPC
// positions live in NpcDatabase and pickup positions in the map bake, both
// Godot-facing. This layer stays engine-free and testable with a fake.
public static class QuestMarkerResolver
{
    // The markers of `questId` that apply to `state` right now. Empty for a
    // quest that isn't in progress — an unstarted quest has nowhere to send the
    // player, and a finished one is finished.
    public static IReadOnlyList<QuestMarker> ActiveMarkers(
        GameState state, uint questId, Action<string> warn = null) =>
        ActiveMarkers(state, QuestCatalog.Get(questId), warn);

    // The same, for a caller that already holds the definition (the journal and
    // the map both do) — and the way a test hands in content the catalog would
    // have rejected.
    public static IReadOnlyList<QuestMarker> ActiveMarkers(
        GameState state, Quest quest, Action<string> warn = null)
    {
        var active = new List<QuestMarker>();
        if (state == null || quest?.Markers == null
            || state.GetQuestState(quest.Id) != QUESTSUCCESSSTATE.InProgress)
        {
            return active;
        }
        var context = new DialogueContext
        {
            State = state,
            LogWarning = warn ?? (_ => { }),
        };
        foreach (var marker in quest.Markers)
        {
            // A marker that fails validation is content the catalog would have
            // rejected at registration; be total anyway, the way the rest of
            // the vocabulary is, and drop it rather than throw mid-play.
            if (QuestMarker.Validate(marker) is { } problem)
            {
                context.Warn($"Quest {quest.Id} marker skipped: {problem}.");
                continue;
            }
            if (DialogueConditions.Evaluate(marker.VisibleWhen, context))
            {
                active.Add(marker);
            }
        }
        return active;
    }

    // The active markers that `locator` can place, in authored order. A marker
    // whose target can't be found — an NPC that never spawned in this area, a
    // pickup in an un-baked chunk — is dropped with a warning: the map says
    // less rather than lying or crashing.
    public static IReadOnlyList<ResolvedQuestMarker> Resolve(
        GameState state, uint questId, IQuestTargetLocator locator, Action<string> warn = null) =>
        Resolve(state, QuestCatalog.Get(questId), locator, warn);

    public static IReadOnlyList<ResolvedQuestMarker> Resolve(
        GameState state, Quest quest, IQuestTargetLocator locator, Action<string> warn = null)
    {
        var resolved = new List<ResolvedQuestMarker>();
        if (quest == null)
        {
            return resolved;
        }
        foreach (var marker in ActiveMarkers(state, quest, warn))
        {
            if (locator == null || !locator.TryLocate(marker.Target, out var location) || location == null)
            {
                warn?.Invoke($"Quest {quest.Id}: no location for marker target '{marker.Target.ToToken()}'.");
                continue;
            }
            resolved.Add(new ResolvedQuestMarker
            {
                QuestId = quest.Id,
                SideQuest = quest.SideQuest,
                Label = marker.Label,
                Target = marker.Target,
                Location = location,
            });
        }
        return resolved;
    }
}

// Where a marker target actually is. Supplied by the Godot side (Phase 2) from
// NpcDatabase and the baked map manifests.
public class QuestTargetLocation
{
    // The chunked area the target sits in, as the map names it — the basename
    // of ChunkManager.ChunkDirectory, e.g. "IntroStation". Empty when the
    // target belongs to a level with no chunk grid (an interior).
    public string AreaName { get; set; } = "";

    // The level scene the target belongs to. What the map matches against a
    // door's TargetScenePath to route an interior objective to its entrance
    // (Phase 4).
    public string ScenePath { get; set; } = "";

    // World position; MapProjection turns it into map pixels.
    public float WorldX { get; set; }
    public float WorldZ { get; set; }
}

// An active marker with a position: what the map draws.
public class ResolvedQuestMarker
{
    public uint QuestId { get; set; }

    // Carried from the quest definition so the map can colour main and side
    // objectives differently without a second catalog lookup.
    public bool SideQuest { get; set; }

    public string Label { get; set; } = "";

    public QuestMarkerTarget Target { get; set; }

    public QuestTargetLocation Location { get; set; }
}

// How the resolver finds out where a target is, without knowing what a chunk
// or an NpcDefinition is. False means "not something I can place" — normal for
// a target in another area, not an error.
public interface IQuestTargetLocator
{
    bool TryLocate(QuestMarkerTarget target, out QuestTargetLocation location);
}
