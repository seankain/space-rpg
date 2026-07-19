using Godot;
using System.Collections.Generic;

// The "Return the Maguffin" fetch quest conversation (ported from the old
// QuestGiverNpc subclass): offers the quest, reminds the player while it's
// running, and takes the Maguffin Cube to complete it. Quest progress lives
// in GameState.Quests, so it persists in saves; transitions here are inline
// until a QuestManager exists (quest plan Phase 2). Dialogue text stays in
// C# until Yarn takes it over (npc-composition plan decisions).
//
// .tres files reference this script by path, so it must not move.
[GlobalClass]
public partial class QuestGiverRole : NpcRole
{
	public override string MenuLabel => "About the missing Maguffin Cube";

	public override DialogueLine BuildDialogue(Npc npc, GameState state)
	{
		var questState = state.GetQuestState(QuestCatalog.ReturnTheMaguffinId);
		var hasCube = state.Inventory.CountOf(ItemCatalog.MaguffinCubeId) > 0;
		return questState switch
		{
			QUESTSUCCESSSTATE.Success => new DialogueLine
			{
				Speaker = npc.DisplayName,
				Text = "The cube's back under lock and key, thanks to you. The station owes you one.",
			},
			QUESTSUCCESSSTATE.InProgress when hasCube => TurnInLine(npc, state),
			QUESTSUCCESSSTATE.InProgress => new DialogueLine
			{
				Speaker = npc.DisplayName,
				Text = "Any sign of my Maguffin Cube? Small, purple, hums like a bad capacitor. It's around here somewhere.",
			},
			_ => OfferLine(npc, state, hasCube),
		};
	}

	private DialogueLine OfferLine(Npc npc, GameState state, bool hasCube)
	{
		var quest = QuestCatalog.Get(QuestCatalog.ReturnTheMaguffinId);
		return new DialogueLine
		{
			Speaker = npc.DisplayName,
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
						? TurnInLine(npc, state)
						: new DialogueLine
						{
							Speaker = npc.DisplayName,
							Text = "Much obliged. It's a small purple cube with a hum you can feel in your teeth — you can't miss it.",
						},
				},
				new DialogueChoice
				{
					Label = "Not now",
					Next = new DialogueLine
					{
						Speaker = npc.DisplayName,
						Text = "Hmph. It won't walk back on its own.",
					},
				},
			},
		};
	}

	private DialogueLine TurnInLine(Npc npc, GameState state)
	{
		return new DialogueLine
		{
			Speaker = npc.DisplayName,
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
