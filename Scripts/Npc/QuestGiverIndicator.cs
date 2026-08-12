using Godot;

// The mark over an NPC with a quest to give (quest-system.md Phase 3): a
// billboarded "!" that appears when this NPC is the authored giver of a quest
// the party could take right now, and goes away the moment they take it.
//
// Which quests those are is QuestManager.AvailableFrom — authored giver, not
// started, prerequisites met — so this node only decides when to ask. It asks
// on entering the tree and whenever a quest moves, which covers taking the
// quest, finishing the one that gated it, and a save being loaded (the level
// is rebuilt, so every indicator is new).
public partial class QuestGiverIndicator : Label3D
{
	// How far above the interaction prompt the mark floats, so the two don't
	// overlap when the player is close enough to see both.
	public const float HeightAbovePrompt = 0.45f;

	// The NpcDefinition.NpcId this indicator speaks for.
	public string NpcId = "";

	public override void _Ready()
	{
		Text = "!";
		Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
		// Same treatment as InteractionPrompt: constant on-screen size, drawn
		// over geometry, so it reads from across the plaza.
		FixedSize = true;
		NoDepthTest = true;
		RenderPriority = 10;
		PixelSize = 0.0008f;
		FontSize = 64;
		OutlineSize = 12;
		Modulate = new Color(0.98f, 0.86f, 0.45f);
		OutlineModulate = new Color(0, 0, 0, 0.85f);

		QuestManager.Moved += OnQuestMoved;
		Refresh();
	}

	public override void _ExitTree()
	{
		QuestManager.Moved -= OnQuestMoved;
	}

	private void OnQuestMoved(QuestMove move)
	{
		if (IsInstanceValid(this))
		{
			Refresh();
		}
	}

	public void Refresh()
	{
		// Only the mark, never the quest's name: what is on offer is the
		// conversation's to say, and a title floating over a stranger's head
		// gives away a beat the writer may want to land themselves.
		Visible = QuestManager.AvailableFrom(SaveManager.Instance?.CurrentState, NpcId).Count > 0;
	}
}
