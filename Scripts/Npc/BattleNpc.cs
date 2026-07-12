using System.Collections.Generic;
using Godot;

// A hostile-ish NPC whose dialogue can start a turn-based battle. Picking
// the fight hands off to BattleManager, which swaps the game into battle
// mode once this conversation closes. Winning despawns the challenger for
// this visit (defeat persistence in world state comes later); losing is a
// game over handled entirely by BattleManager.
public partial class BattleNpc : Npc
{
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
		if (!IsQueuedForDeletion())
		{
			QueueFree();
		}
	}
}
