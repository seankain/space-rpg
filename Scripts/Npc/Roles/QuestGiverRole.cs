using Godot;

// The "Return the Maguffin" fetch quest role. The conversation itself now
// lives in Resources/Dialogue/intro.dockmaster_hale.dialogue.json (dialogue-
// editor plan Phase 2) — the quest-state routing, the take-item/set-quest
// turn-in, and the refuse-at-swordpoint start_battle branch are all authored
// there. This class only supplies the composition menu label; the definition's
// role points DialogueId at the graph.
//
// .tres files reference this script by path, so it must not move.
[GlobalClass]
public partial class QuestGiverRole : NpcRole
{
	public override string MenuLabel => "About the missing Maguffin Cube";
}
