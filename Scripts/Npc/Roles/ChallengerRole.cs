using Godot;

// The pick-a-fight role. The conversation now lives in
// Resources/Dialogue/intro.vex.dialogue.json (dialogue-editor plan Phase 2);
// its "Settle it" choice runs the start_battle:despawn effect, which hands off
// to BattleManager and removes the loser on the player's win (the despawn body
// is in NpcDialogueHost). This class keeps the spawn/availability gating that
// keeps a beaten challenger down, and the composition menu label.
//
// .tres files reference this script by path, so it must not move.
[GlobalClass]
public partial class ChallengerRole : NpcRole
{
	// Whether winning removes the NPC from the world for good (the lone-
	// role default, matching the old BattleNpc). Multi-role NPCs set this
	// false so their other roles keep working after the fight; the graph's
	// start_battle effect carries the matching :despawn arg. The challenge
	// itself goes unavailable either way once they're beaten.
	[Export]
	public bool DespawnOnDefeat { get; set; } = true;

	public override string MenuLabel => "We settle this here";

	// Beaten on an earlier visit (or before a save): stay down instead of
	// respawning with the chunk.
	public override bool ShouldSpawn(NpcDefinition definition, GameState state) =>
		!DespawnOnDefeat || state?.IsNpcDefeated(definition.NpcId) != true;

	public override bool IsAvailable(Npc npc, GameState state) =>
		base.IsAvailable(npc, state) && !state.IsNpcDefeated(npc.NpcId);
}
