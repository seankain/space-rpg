using System;
using Godot;

// Dialogue verbs any role's authored choices can share (npc-composition plan
// Phase 3). Cross-role flexibility comes from actions like these rather than
// role-to-role coupling: a quest giver whose demand ends in a fight uses
// StartBattle directly and needs no ChallengerRole at all.
public static class DialogueActions
{
	// Starts a turn-based battle against this NPC; BattleManager takes over
	// the frame after the current conversation closes, so wire this to a
	// choice with no Next. Victory always records the defeat in
	// GameState.DefeatedNpcs (keyed by NpcId, where quests and role
	// availability checks can see it) before running onWon. The NPC is not
	// despawned here — callers that want the loser gone do it in onWon.
	public static void StartBattle(Npc npc, Action onWon = null)
	{
		BattleManager.StartBattle(npc.NpcId, npc.DisplayName, () =>
		{
			SaveManager.Instance?.CurrentState?.MarkNpcDefeated(npc.NpcId);
			onWon?.Invoke();
		});
	}
}
