using Godot;
using System.Linq;

// The join-the-party role. The conversation now lives in a dialogue graph
// (Resources/Dialogue/intro.rig.dialogue.json,
// intro.chief_marlow_recruit.dialogue.json) whose party_has_room condition
// gates the offer and whose recruit:<id> effect copies the NPC into the party
// (dialogue-editor plan Phase 2; the recruit body itself is in
// NpcDialogueHost). This class keeps the spawn-time veto and the composition
// menu label; PartyCharacterId is the id the recruit effect uses and the key
// the veto checks.
//
// .tres files reference this script by path, so it must not move.
[GlobalClass]
public partial class RecruitRole : NpcRole
{
	// CharacterEntity id this NPC becomes in the party (the player is 1).
	// Doubles as the "already recruited" check when the level reloads, and is
	// the id the graph's recruit:<id> effect assigns. Set per definition; must
	// be unique across recruitables.
	[Export]
	public ulong PartyCharacterId { get; set; } = 2;

	public override string MenuLabel => "About joining the crew";

	// Recruited on an earlier visit (or before a save): they're in the party
	// now, not standing around the docks.
	public override bool ShouldSpawn(NpcDefinition definition, GameState state) =>
		state?.Party?.Any(m => m.Id == PartyCharacterId) != true;
}
