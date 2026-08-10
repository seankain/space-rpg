using System;

// The "which quest is the player following" signal (quest-markers.md Phase 3).
// The value itself lives on GameState (it is saved); this is only how the views
// hear that it moved.
//
// Static for the same reason GameEventLog.Recorded is: the listeners — the
// Quests tab, the Map tab, a future HUD compass — outlive any one GameState,
// and loading a save swaps the state instance underneath them, so a
// per-instance event would go stale on every load. Subscribers must
// unsubscribe when they leave the tree or they leak.
public static class QuestTracking
{
    // The newly tracked quest id, or 0 when tracking was cleared.
    public static event Action<uint> Changed;

    // Called by GameState.SetTrackedQuest — the one place tracking moves.
    public static void NotifyChanged(uint questId) => Changed?.Invoke(questId);
}
