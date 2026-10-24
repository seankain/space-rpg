using Godot;

// A place in the world that moves a quest along (quest-system.md Phase 2): the
// player walks in, and the quest reaches the stage this trigger stands for.
// That covers the beats no conversation owns — "reach the cargo bay", "get
// through the vent" — which until now had nothing to advance them at all.
//
// Fires once in the sense that matters, without remembering anything: it asks
// for "at least this stage" on a quest that is in progress, so walking back
// through, reloading a save made afterwards, or streaming the chunk out and in
// again all move nothing. There is no per-trigger flag in the save file.
//
// The stage is named rather than "the next one": a trigger is a fixed place
// tied to a specific beat, and "advance by one from wherever you are" would
// fire again every time the player walked back through.
public partial class QuestTrigger : Area3D
{
	// The quest this place belongs to, and the stage standing here reaches.
	[Export]
	public uint QuestId;

	[Export]
	public uint StageNumber = 1;

	public override void _Ready()
	{
		// A trigger naming a quest or stage that doesn't exist would sit there
		// doing nothing, which is the hardest kind of content bug to notice.
		// The catalog is loaded by now, so say so at scene load instead.
		var quest = QuestCatalog.Get(QuestId);
		if (quest == null)
		{
			GD.PushWarning($"QuestTrigger '{Name}' names unknown quest id {QuestId}.");
		}
		else if (quest.GetStage(StageNumber) == null)
		{
			GD.PushWarning(
				$"QuestTrigger '{Name}' names stage {StageNumber} of quest {QuestId} "
				+ $"('{quest.Title}'), which declares {quest.Stages.Count}.");
		}

		BodyEntered += body =>
		{
			if (body.IsInGroup(Player.GroupName))
			{
				QuestManager.ReachStage(SaveManager.Instance?.CurrentState, QuestId, StageNumber);
			}
		};
	}
}
