using System.Collections.Generic;
using Godot;

// A hostile-ish NPC whose dialogue can start a turn-based battle. Picking
// the fight hands off to BattleManager, which swaps the game into battle
// mode once this conversation closes. Winning records the defeat in
// GameState.DefeatedNpcs (so the challenger stays down across saves and
// reloads, and quests can check for it) and despawns the challenger;
// losing is a game over handled entirely by BattleManager.
public partial class BattleNpc : Npc
{
	public override void _Ready()
	{
		// Beaten on an earlier visit (or before a save): stay down instead
		// of respawning with the chunk.
		if (SaveManager.Instance?.CurrentState?.IsNpcDefeated(DisplayName) == true)
		{
			QueueFree();
			return;
		}
		base._Ready();
	}

	protected override void OnInteract()
	{
		var challenge = new DialogueLine
		{
			Speaker = DisplayName,
			Text = "This is my stretch of deck, groundsider. Turn around, or we settle it right here.",
			Choices = new List<DialogueChoice>
			{
				new DialogueChoice
				{
					Label = "Settle it",
					// No Next: the conversation ends and the deferred battle
					// takes over the frame after.
					Action = () => BattleManager.StartBattle(DisplayName, OnBattleWon),
				},
				new DialogueChoice
				{
					Label = "Walk away",
					Next = new DialogueLine
					{
						Speaker = DisplayName,
						Text = "Smart. Keep walking.",
					},
				},
			},
		};
		DialogueManager.Instance.Start(challenge, ShowPromptIfPlayerInRange);
	}

	private void OnBattleWon()
	{
		SaveManager.Instance?.CurrentState?.MarkNpcDefeated(DisplayName);
		if (!IsQueuedForDeletion())
		{
			QueueFree();
		}
	}
}
