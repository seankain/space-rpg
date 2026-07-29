using Godot;
using System.Collections.Generic;

// Bridges the scene-touching dialogue effects (start_battle, open_shop,
// recruit, play_anim) to a live Npc (dialogue-editor plan Phase 2). The pure
// verbs (give_item, set_quest, …) mutate GameState directly inside
// DialogueEffects; these need the running node, so they live here where a Godot
// type can reach the scene tree. The first three bodies are relocated role
// lambdas — the same battle hand-off, shop open, and recruit-and-follow the
// role classes used to run inline.
public sealed class NpcDialogueHost : IDialogueEffectHost
{
	private readonly Npc npc;
	private readonly GameState state;

	public NpcDialogueHost(Npc npc, GameState state)
	{
		this.npc = npc;
		this.state = state;
	}

	// The conversation ends and BattleManager takes over the frame after (the
	// choice that runs this has no Next). Victory records the defeat in
	// GameState.DefeatedNpcs via DialogueActions; despawnOnDefeat removes the
	// loser from the world, reproducing ChallengerRole's lone-challenger
	// policy from the start_battle:despawn effect.
	public void StartBattle(bool despawnOnDefeat)
	{
		DialogueActions.StartBattle(npc, despawnOnDefeat ? DespawnNpc : (System.Action)null);
	}

	private void DespawnNpc()
	{
		if (GodotObject.IsInstanceValid(npc) && !npc.IsQueuedForDeletion())
		{
			npc.QueueFree();
		}
	}

	// Open the trading screen for this NPC's Merchant. DialogueManager.End()
	// recaptures the mouse after the choice action runs, so defer the open a
	// frame to win that race (same trick the old ShopkeeperRole used).
	public void OpenShop()
	{
		if (npc.GetTree().GetFirstNodeInGroup(ShopMenu.GroupName) is not ShopMenu shopMenu)
		{
			GD.PushWarning($"Shopkeeper '{npc.DisplayName}' found no ShopMenu.");
			return;
		}
		if (npc.GetRoleState<Merchant>() is not { } merchant)
		{
			GD.PushWarning($"Shopkeeper '{npc.DisplayName}' has no Merchant runtime state.");
			return;
		}
		Callable.From(() => shopMenu.Open(merchant, npc.ShowPromptIfPlayerInRange)).CallDeferred();
	}

	// Gesture on the speaking NPC's rig (npc-dialogue-yarn.md Phase 4). The
	// only verb here with no consequence beyond what the player sees.
	public void PlayAnimation(string clip, bool loop) => npc.PlayDialogueAnimation(clip, loop);

	// Copy this NPC into the party as the given character id, hand the world
	// body off to a follower, and despawn it when the conversation closes
	// (party plan Phase 1/2, relocated from RecruitRole.Join). The stat block
	// is the recruit template every intro joiner shared.
	public void Recruit(ulong partyCharacterId)
	{
		var party = new PartyManager(state.Party);
		var member = new CharacterEntity
		{
			Id = partyCharacterId,
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
