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

	// Open the trading screen for this NPC's Merchant. Deferred a frame so the
	// conversation finishes tearing down first — the shop opens against a
	// settled window stack rather than from inside the dialogue's own teardown.
	// (It no longer races the mouse: UiWindowManager derives the pointer from
	// whatever is open, so there is no recapture to beat.)
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

	// The stat template every intro joiner shared before NPCs carried their
	// own sheet. Definitions without a Stats block (every .tres authored
	// before the in-world NPC editor) still recruit exactly as they did.
	private static readonly NpcStatBlock DefaultRecruitStats = new()
	{
		Level = 1,
		MaxHealthPoints = 8,
		MaxMagicPoints = 3,
		Strength = 6,
		Intelligence = 4,
		Constitution = 6,
		Dexterity = 7,
		Wisdom = 4,
		Charisma = 5,
	};

	// Copy this NPC into the party as the given character id, hand the world
	// body off to a follower, and despawn it when the conversation closes
	// (party plan Phase 1/2, relocated from RecruitRole.Join). The stats are
	// the NPC's own sheet when it has one — the editor's stat block is what
	// the joiner walks in with.
	public void Recruit(ulong partyCharacterId)
	{
		var party = new PartyManager(state.Party);
		var sheet = npc.Definition?.Stats ?? DefaultRecruitStats;
		var member = new CharacterEntity
		{
			Id = partyCharacterId,
			Name = npc.DisplayName,
			Level = sheet.Level,
			HealthPoints = sheet.MaxHealthPoints,
			MaxHealthPoints = sheet.MaxHealthPoints,
			MagicPoints = sheet.MaxMagicPoints,
			MaxMagicPoints = sheet.MaxMagicPoints,
			Stats = sheet.ToCharacterStats(),
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
		state.RecordEvent(GameEventKind.Party, $"{npc.DisplayName} joined the party.", notify: true);
	}
}
