using Godot;

// The "Clear the Deck" bounty role. The conversation now lives in
// Resources/Dialogue/intro.chief_marlow.dialogue.json (dialogue-editor plan
// Phase 2): the quest-state routing, the npc_defeated:intro.vex gating on
// whether Vex is already down, and the give-keycard/set-quest turn-in are all
// authored there. This class only supplies the composition menu label; the
// definition's role points DialogueId at the graph.
//
// .tres files reference this script by path, so it must not move.
[GlobalClass]
public partial class BountyGiverRole : NpcRole
{
	public override string MenuLabel => "About the deck bounty";
}
