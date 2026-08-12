using Godot;

// Autoload (registered in project.godot). The tracked quest, over the world:
// its title and the one line saying what the player is doing right now
// (quest-system.md Phase 3). Until this, "what am I doing" lived only in the
// in-game menu — the journal's details panel and the Map tab's objective line —
// so following a quest meant opening a menu to remember it.
//
// Built in code like the toasts and the dialogue box, so it needs no
// hand-authored scene. What it says is decided by the engine-free
// QuestObjectives; this is the label.
public partial class QuestHud : CanvasLayer
{
	// Corner inset, matching the toasts' margin on the other side.
	private const int Margin = 24;

	private const int ColumnWidth = 440;

	private VBoxContainer column;
	private Label titleLabel;
	private Label objectiveLabel;

	// What is on screen, so the labels are rebuilt when the answer changes
	// rather than every frame.
	private uint shownQuestId;
	private uint shownStage;
	private bool shownHidden;
	private bool dirty = true;

	public override void _Ready()
	{
		Layer = UiLayers.QuestHud;
		// A battle pauses the tree, and this has to be able to take itself off
		// the screen when one starts.
		ProcessMode = ProcessModeEnum.Always;
		BuildUi();
		QuestManager.Moved += OnQuestMoved;
		QuestTracking.Changed += OnTrackingChanged;
	}

	public override void _ExitTree()
	{
		QuestManager.Moved -= OnQuestMoved;
		QuestTracking.Changed -= OnTrackingChanged;
	}

	private void OnQuestMoved(QuestMove move) => dirty = true;

	private void OnTrackingChanged(uint questId) => dirty = true;

	// The two signals above cover the quest moving and the player picking a
	// different one to follow. The two cases with no signal to hang on — a save
	// loaded under us, and a menu or conversation opening over us — are a
	// three-field compare per frame, which is cheaper than the plumbing it
	// would take to announce them.
	public override void _Process(double delta)
	{
		var state = SaveManager.Instance?.CurrentState;
		// The HUD belongs to play: a conversation, a menu or a battle covers it.
		var hidden = state == null || UiWindowManager.BlocksGameplay;
		var questId = state?.TrackedQuestId ?? 0;
		var stage = state?.GetQuestStage(questId) ?? 0;
		if (!dirty && hidden == shownHidden && questId == shownQuestId && stage == shownStage)
		{
			return;
		}
		dirty = false;
		shownHidden = hidden;
		shownQuestId = questId;
		shownStage = stage;
		Refresh(hidden ? null : state);
	}

	private void Refresh(GameState state)
	{
		var headline = QuestObjectives.Tracked(state, GD.PushWarning);
		column.Visible = headline != null;
		if (headline == null)
		{
			return;
		}
		titleLabel.Text = headline.Title;
		objectiveLabel.Text = headline.Objective;
		// A quest with nothing to point at right now shows as its title alone,
		// not as a title with a blank line under it.
		objectiveLabel.Visible = !string.IsNullOrEmpty(headline.Objective);
	}

	private void BuildUi()
	{
		// Bottom-left, growing up: clear of the toasts (top-right) and the
		// dialogue box (bottom center). It shares the corner with the editor
		// HUD, which is only up while gameplay is blocked — and this hides
		// then.
		column = new VBoxContainer
		{
			AnchorLeft = 0f,
			AnchorRight = 0f,
			AnchorTop = 1f,
			AnchorBottom = 1f,
			OffsetLeft = Margin,
			OffsetRight = Margin + ColumnWidth,
			OffsetTop = -Margin,
			OffsetBottom = -Margin,
			GrowHorizontal = Control.GrowDirection.End,
			GrowVertical = Control.GrowDirection.Begin,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false,
		};
		column.AddThemeConstantOverride("separation", 2);

		titleLabel = BuildLine(new Color(0.98f, 0.86f, 0.45f), 18);
		objectiveLabel = BuildLine(new Color(0.86f, 0.9f, 0.96f), 15);
		column.AddChild(titleLabel);
		column.AddChild(objectiveLabel);
		AddChild(column);
	}

	// Outlined like a toast, because this one draws straight over the world
	// rather than over a panel.
	private static Label BuildLine(Color color, int fontSize)
	{
		var label = new Label
		{
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		label.AddThemeColorOverride("font_color", color);
		label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
		label.AddThemeConstantOverride("outline_size", 4);
		label.AddThemeFontSizeOverride("font_size", fontSize);
		return label;
	}
}
