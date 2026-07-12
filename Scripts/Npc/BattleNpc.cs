using System.Collections.Generic;

// A hostile-ish NPC whose dialogue can start a turn-based battle. The battle
// itself is stubbed behind BattleManager.StartBattle and is picked up by the
// combat task; this NPC only owns the challenge conversation.
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
					Action = () => BattleManager.StartBattle(DisplayName),
					// Placeholder beat until BattleManager actually swaps to a
					// battle scene — then this line goes away.
					Next = new DialogueLine
					{
						Text = $"(The turn-based battle system isn't built yet. {DisplayName} holsters his cutter... for now.)",
					},
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
}
