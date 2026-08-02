using Godot;

// Autoload (registered in project.godot). The transient half of the event log:
// whenever an entry is recorded with Notify set — a pickup, a purchase, credits
// earned — a small line slides into the top-right corner, holds, and fades. The
// permanent record lives in GameState.EventLog and is browsed in the in-game
// menu's Log tab; this is only the "you just got something" nudge.
//
// Built in code like the dialogue box and the editor HUD, so the toasts need no
// hand-authored scene.
public partial class EventToasts : CanvasLayer
{
	public static EventToasts Instance { get; private set; }

	private const double FadeInSeconds = 0.15;
	private const double HoldSeconds = 2.6;
	private const double FadeOutSeconds = 0.6;

	// Older toasts are dropped past this so a burst (loot a stack, sell a
	// bagful) can't wall off the screen.
	private const int MaxVisible = 5;

	private const int ColumnWidth = 380;

	private VBoxContainer column;

	public override void _Ready()
	{
		Instance = this;
		Layer = UiLayers.Toasts;
		// Battles pause the tree; a victory's toast still has to animate.
		ProcessMode = ProcessModeEnum.Always;
		BuildUi();
		GameEventLog.Recorded += OnEventRecorded;
	}

	public override void _ExitTree()
	{
		GameEventLog.Recorded -= OnEventRecorded;
	}

	private void OnEventRecorded(GameEvent entry)
	{
		if (entry is { Notify: true } && IsInstanceValid(this))
		{
			Show(entry.Text, GameEventDisplay.ColorOf(entry.Kind));
		}
	}

	// Also usable directly for one-off messages that aren't worth a log entry.
	public void Show(string text, Color color)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}
		while (column.GetChildCount() >= MaxVisible)
		{
			// QueueFree is deferred, so pull the node out of the column now or
			// the toasts queued behind it this frame still count it as a child.
			var oldest = column.GetChild(0);
			column.RemoveChild(oldest);
			oldest.QueueFree();
		}

		var panel = new PanelContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Modulate = new Color(1, 1, 1, 0),
		};
		panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.05f, 0.06f, 0.09f, 0.78f),
			BorderColor = new Color(color, 0.55f),
			BorderWidthLeft = 3,
			CornerRadiusTopLeft = 4,
			CornerRadiusTopRight = 4,
			CornerRadiusBottomLeft = 4,
			CornerRadiusBottomRight = 4,
			ContentMarginLeft = 10,
			ContentMarginRight = 10,
			ContentMarginTop = 6,
			ContentMarginBottom = 6,
		});

		var label = new Label
		{
			Text = text,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		label.AddThemeColorOverride("font_color", color);
		label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
		label.AddThemeConstantOverride("outline_size", 4);
		label.AddThemeFontSizeOverride("font_size", 16);
		panel.AddChild(label);
		column.AddChild(panel);

		// Bound to the toast, so trimming it early kills its animation too.
		var tween = panel.CreateTween();
		tween.TweenProperty(panel, "modulate:a", 1.0f, FadeInSeconds);
		tween.TweenInterval(HoldSeconds);
		tween.TweenProperty(panel, "modulate:a", 0.0f, FadeOutSeconds);
		tween.TweenCallback(Callable.From(panel.QueueFree));
	}

	private void BuildUi()
	{
		// Top-right corner, growing down: clear of the dialogue box (bottom
		// center), the interaction prompt (world space), and the editor HUD
		// (bottom left).
		column = new VBoxContainer
		{
			AnchorLeft = 1f,
			AnchorRight = 1f,
			AnchorTop = 0f,
			AnchorBottom = 0f,
			OffsetLeft = -(ColumnWidth + 24),
			OffsetRight = -24,
			OffsetTop = 24,
			OffsetBottom = 24,
			GrowHorizontal = Control.GrowDirection.Begin,
			GrowVertical = Control.GrowDirection.End,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		column.AddThemeConstantOverride("separation", 6);
		AddChild(column);
	}
}
