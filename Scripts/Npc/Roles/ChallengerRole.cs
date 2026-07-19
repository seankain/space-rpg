using Godot;
using System.Collections.Generic;

// The pick-a-fight conversation (ported from the old BattleNpc subclass):
// dialogue that can start a turn-based battle via BattleManager, which swaps
// the game into battle mode once the conversation closes. Winning records
// the defeat in GameState.DefeatedNpcs (so the challenger stays down across
// saves and reloads, and quests can check for it); losing is a game over
// handled entirely by BattleManager.
//
// .tres files reference this script by path, so it must not move.
[GlobalClass]
public partial class ChallengerRole : NpcRole
{
	// Whether winning removes the NPC from the world for good (the lone-
	// role default, matching the old BattleNpc). Multi-role NPCs set this
	// false so their other roles keep working after the fight; the
	// challenge itself goes unavailable either way once they're beaten.
	[Export]
	public bool DespawnOnDefeat { get; set; } = true;

	public override string MenuLabel => "We settle this here";

	// Beaten on an earlier visit (or before a save): stay down instead of
	// respawning with the chunk.
	public override bool ShouldSpawn(NpcDefinition definition, GameState state) =>
		!DespawnOnDefeat || state?.IsNpcDefeated(definition.NpcId) != true;

	public override bool IsAvailable(Npc npc, GameState state) =>
		base.IsAvailable(npc, state) && !state.IsNpcDefeated(npc.NpcId);

	public override DialogueLine BuildDialogue(Npc npc, GameState state)
	{
		return new DialogueLine
		{
			Speaker = npc.DisplayName,
			Text = "This is my stretch of deck, groundsider. Turn around, or we settle it right here.",
			Choices = new List<DialogueChoice>
			{
				new DialogueChoice
				{
					Label = "Settle it",
					// No Next: the conversation ends and the deferred battle
					// takes over the frame after.
					Action = () => BattleManager.StartBattle(npc.NpcId, npc.DisplayName, () => OnBattleWon(npc)),
				},
				new DialogueChoice
				{
					Label = "Walk away",
					Next = new DialogueLine
					{
						Speaker = npc.DisplayName,
						Text = "Smart. Keep walking.",
					},
				},
			},
		};
	}

	private void OnBattleWon(Npc npc)
	{
		SaveManager.Instance?.CurrentState?.MarkNpcDefeated(npc.NpcId);
		if (DespawnOnDefeat && GodotObject.IsInstanceValid(npc) && !npc.IsQueuedForDeletion())
		{
			npc.QueueFree();
		}
	}
}
