using Godot;
using System.Collections.Generic;
using System.Linq;

// The join-the-party conversation (ported from the old RecruitNpc subclass;
// party plan Phase 1: recruiting copies a definition into GameState.Party as
// a live CharacterEntity). Once recruited the world NPC despawns when the
// conversation closes and a PartyMemberFollower takes over walking behind
// the leader (party plan Phase 2).
//
// .tres files reference this script by path, so it must not move.
[GlobalClass]
public partial class RecruitRole : NpcRole
{
	// CharacterEntity id this NPC becomes in the party (the player is 1).
	// Doubles as the "already recruited" check when the level reloads. Set
	// per definition; must be unique across recruitables.
	[Export]
	public ulong PartyCharacterId { get; set; } = 2;

	public override string MenuLabel => "About joining the crew";

	// Recruited on an earlier visit (or before a save): they're in the party
	// now, not standing around the docks.
	public override bool ShouldSpawn(NpcDefinition definition, GameState state) =>
		state?.Party?.Any(m => m.Id == PartyCharacterId) != true;

	public override DialogueLine BuildDialogue(Npc npc, GameState state)
	{
		if (new PartyManager(state.Party).IsFull)
		{
			return new DialogueLine
			{
				Speaker = npc.DisplayName,
				Text = "Looks like you're already running a full crew. Come find me if a spot opens up.",
			};
		}
		return new DialogueLine
		{
			Speaker = npc.DisplayName,
			Text = "Hey! You look like you're headed somewhere more interesting than this dock. Can I join up with you?",
			Choices = new List<DialogueChoice>
			{
				new DialogueChoice
				{
					Label = "Yes",
					Action = () => Join(npc, state),
					Next = new DialogueLine
					{
						Speaker = npc.DisplayName,
						Text = "You won't regret it. Lead the way, boss!",
					},
				},
				new DialogueChoice
				{
					Label = "No",
					Next = new DialogueLine
					{
						Speaker = npc.DisplayName,
						Text = "Suit yourself. I'll be right here if you change your mind.",
					},
				},
			},
		};
	}

	private void Join(Npc npc, GameState state)
	{
		var party = new PartyManager(state.Party);
		var member = new CharacterEntity
		{
			Id = PartyCharacterId,
			Name = npc.DisplayName,
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
		// The world body QueueFrees when the dialogue closes; the follower
		// steps in where the NPC was standing.
		npc.DespawnWhenDialogueEnds();
		if (npc.GetTree().GetFirstNodeInGroup(Player.GroupName) is Node player)
		{
			PartyMemberFollower.Spawn(player.GetParent(), member, party.Members.Count - 1, npc.GlobalPosition);
		}
		// TODO: HUD toast once PlayerHud grows one (same note as Pickup).
		GD.Print($"{npc.DisplayName} joined the party.");
	}
}
