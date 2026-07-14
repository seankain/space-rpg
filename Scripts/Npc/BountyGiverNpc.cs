using Godot;
using System.Collections.Generic;

// Quest-giver NPC for the "Clear the Deck" bounty: asks the player to defeat
// Vex (the plaza's BattleNpc) and pays out a Maintenance Keycard once he's
// down. The defeat is read from GameState.DefeatedNpcs — written by BattleNpc
// on victory — so turning in works whether Vex fell before or after taking
// the bounty. Transitions are inline until a QuestManager exists (quest plan
// Phase 2), same as QuestGiverNpc.
public partial class BountyGiverNpc : Npc
{
	// Stable NpcId of the BattleNpc this bounty targets — data, not code:
	// set on the spawned scene variant (Scenes/Npc/BountyGiverNpc.tscn), so
	// EnemyCatalog and DefeatedNpcs follow the id even if Vex is renamed.
	[Export]
	public string TargetNpcId = "intro.vex";

	protected override void OnInteract()
	{
		var state = SaveManager.Instance?.CurrentState;
		if (state == null)
		{
			GD.PushWarning($"BountyGiverNpc '{DisplayName}' has no game state to track the bounty in.");
			return;
		}
		var questState = state.GetQuestState(QuestCatalog.ClearTheDeckId);
		var targetDown = state.IsNpcDefeated(TargetNpcId);
		var line = questState switch
		{
			QUESTSUCCESSSTATE.Success => new DialogueLine
			{
				Speaker = DisplayName,
				Text = "Deck's been quiet since you put Vex on the plates. Keep that keycard handy — it opens more doors than you'd think.",
			},
			QUESTSUCCESSSTATE.InProgress when targetDown => TurnInLine(state),
			QUESTSUCCESSSTATE.InProgress => new DialogueLine
			{
				Speaker = DisplayName,
				Text = "Vex is still strutting around the plaza like he owns it. The keycard's yours the moment that changes.",
			},
			_ => OfferLine(state, targetDown),
		};
		DialogueManager.Instance.Start(line, ShowPromptIfPlayerInRange);
	}

	private DialogueLine OfferLine(GameState state, bool targetDown)
	{
		var quest = QuestCatalog.Get(QuestCatalog.ClearTheDeckId);
		return new DialogueLine
		{
			Speaker = DisplayName,
			Text = targetDown
				// Vex already lost before the bounty was ever taken; own up to
				// it and pay out through the same accept path.
				? "Word is somebody already put Vex on the deck plates. That was you, wasn't it? I was about to post a bounty on him — say the word and it's yours."
				: "See that red-suited thug by the plaza? Vex. He's been shaking down every courier on this deck and my hands are tied. Put him down a peg and there's a maintenance keycard in it for you.",
			Choices = new List<DialogueChoice>
			{
				new DialogueChoice
				{
					Label = targetDown ? "That was me" : "I'll handle Vex",
					Action = () =>
					{
						state.SetQuestState(QuestCatalog.ClearTheDeckId, QUESTSUCCESSSTATE.InProgress);
						// TODO: HUD quest toast + journal entry (quest plan Phase 3).
						GD.Print($"Quest started: {quest.Title}");
					},
					// Already-beaten challengers pay out on the spot.
					Next = targetDown
						? TurnInLine(state)
						: new DialogueLine
						{
							Speaker = DisplayName,
							Text = "Watch yourself — he doesn't fight fair, and he keeps a drone at his back. Come see me when it's done.",
						},
				},
				new DialogueChoice
				{
					Label = "Not my problem",
					Next = new DialogueLine
					{
						Speaker = DisplayName,
						Text = "Then keep your credits out of his reach, and don't say I didn't warn you.",
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
			Text = "So the deck finally goes quiet. A deal's a deal — here's the maintenance keycard. It'll open doors most people never get to see.",
			OnShown = () =>
			{
				state.Inventory.Add(ItemCatalog.MaintenanceKeycardId);
				state.SetQuestState(QuestCatalog.ClearTheDeckId, QUESTSUCCESSSTATE.Success);
				GD.Print($"Quest completed: {QuestCatalog.Get(QuestCatalog.ClearTheDeckId).Title}");
			},
		};
	}
}
