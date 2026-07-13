using Godot;
using System.Collections.Generic;
using System.Linq;

// An NPC who asks to join the party (party plan Phase 1: recruiting copies a
// definition into GameState.Party as a live CharacterEntity). Once recruited
// the world capsule despawns and a PartyMemberFollower takes over walking
// behind the leader (party plan Phase 2).
public partial class RecruitNpc : Npc
{
	// CharacterEntity id this NPC becomes in the party (the player is 1).
	// Doubles as the "already recruited" check when the level reloads.
	public const ulong CharacterId = 2;

	private bool joined;

	public override void _Ready()
	{
		// Recruited on an earlier visit (or before a save): he's in the party
		// now, not standing around the docks.
		if (SaveManager.Instance?.CurrentState?.Party?.Any(m => m.Id == CharacterId) == true)
		{
			QueueFree();
			return;
		}
		base._Ready();
	}

	protected override void OnInteract()
	{
		var state = SaveManager.Instance?.CurrentState;
		if (state != null && new PartyManager(state.Party).IsFull)
		{
			DialogueManager.Instance.Start(new DialogueLine
			{
				Speaker = DisplayName,
				Text = "Looks like you're already running a full crew. Come find me if a spot opens up.",
			}, ShowPromptIfPlayerInRange);
			return;
		}
		var ask = new DialogueLine
		{
			Speaker = DisplayName,
			Text = "Hey! You look like you're headed somewhere more interesting than this dock. Can I join up with you?",
			Choices = new List<DialogueChoice>
			{
				new DialogueChoice
				{
					Label = "Yes",
					Action = Join,
					Next = new DialogueLine
					{
						Speaker = DisplayName,
						Text = "You won't regret it. Lead the way, boss!",
					},
				},
				new DialogueChoice
				{
					Label = "No",
					Next = new DialogueLine
					{
						Speaker = DisplayName,
						Text = "Suit yourself. I'll be right here if you change your mind.",
					},
				},
			},
		};
		DialogueManager.Instance.Start(ask, () =>
		{
			if (joined)
			{
				QueueFree();
			}
			else
			{
				ShowPromptIfPlayerInRange();
			}
		});
	}

	private void Join()
	{
		var state = SaveManager.Instance?.CurrentState;
		if (state == null)
		{
			GD.PushWarning($"RecruitNpc '{DisplayName}' cannot join: no game state.");
			return;
		}
		var party = new PartyManager(state.Party);
		var member = new CharacterEntity
		{
			Id = CharacterId,
			Name = DisplayName,
			Level = 1,
			HealthPoints = 8,
			MaxHealthPoints = 8,
			MagicPoints = 3,
			MaxMagicPoints = 3,
			Stats = new CharacterStats
			{
				Strength = 6,
				Intelligence = 4,
				Constitution = 6,
				Dexterity = 7,
				Wisdom = 4,
				Charisma = 5,
			},
			EquipSlots = new CharacterEquipSlots(),
			ActiveStatusEffects = new List<ActiveStatusEffect>(),
		};
		if (!party.TryAddMember(member))
		{
			return;
		}
		joined = true;
		// The capsule QueueFrees when the dialogue closes; the follower body
		// steps in where the NPC was standing.
		if (GetTree().GetFirstNodeInGroup(Player.GroupName) is Node player)
		{
			PartyMemberFollower.Spawn(player.GetParent(), member, party.Members.Count - 1, GlobalPosition);
		}
		// TODO: HUD toast once PlayerHud grows one (same note as Pickup).
		GD.Print($"{DisplayName} joined the party.");
	}
}
