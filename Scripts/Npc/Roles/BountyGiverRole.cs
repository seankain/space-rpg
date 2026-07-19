using Godot;
using System.Collections.Generic;

// The "Clear the Deck" bounty conversation (ported from the old
// BountyGiverNpc subclass): asks the player to defeat a challenger NPC and
// pays out a Maintenance Keycard once they're down. The defeat is read from
// GameState.DefeatedNpcs — written by ChallengerRole on victory — so turning
// in works whether the target fell before or after taking the bounty.
// Transitions are inline until a QuestManager exists (quest plan Phase 2).
//
// .tres files reference this script by path, so it must not move.
[GlobalClass]
public partial class BountyGiverRole : NpcRole
{
	// Stable NpcId of the challenger this bounty targets — set in the
	// definition's .tres, so EnemyCatalog and DefeatedNpcs follow the id
	// even if the target is renamed.
	[Export]
	public string TargetNpcId { get; set; } = "intro.vex";

	public override string MenuLabel => "About the deck bounty";

	public override DialogueLine BuildDialogue(Npc npc, GameState state)
	{
		var questState = state.GetQuestState(QuestCatalog.ClearTheDeckId);
		var targetDown = state.IsNpcDefeated(TargetNpcId);
		return questState switch
		{
			QUESTSUCCESSSTATE.Success => new DialogueLine
			{
				Speaker = npc.DisplayName,
				Text = "Deck's been quiet since you put Vex on the plates. Keep that keycard handy — it opens more doors than you'd think.",
			},
			QUESTSUCCESSSTATE.InProgress when targetDown => TurnInLine(npc, state),
			QUESTSUCCESSSTATE.InProgress => new DialogueLine
			{
				Speaker = npc.DisplayName,
				Text = "Vex is still strutting around the plaza like he owns it. The keycard's yours the moment that changes.",
			},
			_ => OfferLine(npc, state, targetDown),
		};
	}

	private DialogueLine OfferLine(Npc npc, GameState state, bool targetDown)
	{
		var quest = QuestCatalog.Get(QuestCatalog.ClearTheDeckId);
		return new DialogueLine
		{
			Speaker = npc.DisplayName,
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
						? TurnInLine(npc, state)
						: new DialogueLine
						{
							Speaker = npc.DisplayName,
							Text = "Watch yourself — he doesn't fight fair, and he keeps a drone at his back. Come see me when it's done.",
						},
				},
				new DialogueChoice
				{
					Label = "Not my problem",
					Next = new DialogueLine
					{
						Speaker = npc.DisplayName,
						Text = "Then keep your credits out of his reach, and don't say I didn't warn you.",
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
