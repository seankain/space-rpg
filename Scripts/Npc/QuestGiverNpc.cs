using Godot;
using System.Collections.Generic;

// Quest-giver NPC for the "Return the Maguffin" fetch quest: offers the quest,
// reminds the player while it's running, and takes the Maguffin Cube to
// complete it. Quest progress lives in GameState.Quests, so it persists in
// saves; transitions here are inline until a QuestManager exists (quest plan
// Phase 2).
public partial class QuestGiverNpc : Npc
{
	protected override void OnInteract()
	{
		var state = SaveManager.Instance?.CurrentState;
		if (state == null)
		{
			GD.PushWarning($"QuestGiverNpc '{DisplayName}' has no game state to track the quest in.");
			return;
		}
		var questState = state.GetQuestState(QuestCatalog.ReturnTheMaguffinId);
		var hasCube = state.Inventory.CountOf(ItemCatalog.MaguffinCubeId) > 0;
		var line = questState switch
		{
			QUESTSUCCESSSTATE.Success => new DialogueLine
			{
				Speaker = DisplayName,
				Text = "The cube's back under lock and key, thanks to you. The station owes you one.",
			},
			QUESTSUCCESSSTATE.InProgress when hasCube => TurnInLine(state),
			QUESTSUCCESSSTATE.InProgress => new DialogueLine
			{
				Speaker = DisplayName,
				Text = "Any sign of my Maguffin Cube? Small, purple, hums like a bad capacitor. It's around here somewhere.",
			},
			_ => OfferLine(state, hasCube),
		};
		DialogueManager.Instance.Start(line, ShowPromptIfPlayerInRange);
	}

	private DialogueLine OfferLine(GameState state, bool hasCube)
	{
		var quest = QuestCatalog.Get(QuestCatalog.ReturnTheMaguffinId);
		return new DialogueLine
		{
			Speaker = DisplayName,
			Text = "A courier fumbled my Maguffin Cube somewhere on this deck and I can't leave my post. Would you track it down for me?",
			Choices = new List<DialogueChoice>
			{
				new DialogueChoice
				{
					Label = "I'll find it",
					Action = () =>
					{
						state.SetQuestState(QuestCatalog.ReturnTheMaguffinId, QUESTSUCCESSSTATE.InProgress);
						// TODO: HUD quest toast + journal entry (quest plan Phase 3).
						GD.Print($"Quest started: {quest.Title}");
					},
					// If the player already scooped it up, hand it straight over.
					Next = hasCube
						? TurnInLine(state)
						: new DialogueLine
						{
							Speaker = DisplayName,
							Text = "Much obliged. It's a small purple cube with a hum you can feel in your teeth — you can't miss it.",
						},
				},
				new DialogueChoice
				{
					Label = "Not now",
					Next = new DialogueLine
					{
						Speaker = DisplayName,
						Text = "Hmph. It won't walk back on its own.",
					},
				},
			},
		};
	}

	private DialogueLine TurnInLine(GameState state)
	{
		return new DialogueLine
		{
			Speaker = DisplayName,
			Text = "That hum — you found it! Hand it over... yes, that's the one. You have my thanks, courier.",
			OnShown = () =>
			{
				if (!state.Inventory.Remove(ItemCatalog.MaguffinCubeId))
				{
					GD.PushWarning("Maguffin turn-in ran without the cube in inventory.");
					return;
				}
				state.SetQuestState(QuestCatalog.ReturnTheMaguffinId, QUESTSUCCESSSTATE.Success);
				GD.Print($"Quest completed: {QuestCatalog.Get(QuestCatalog.ReturnTheMaguffinId).Title}");
			},
		};
	}
}
