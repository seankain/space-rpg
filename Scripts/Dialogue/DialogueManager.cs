using Godot;
using System;

// Autoload (registered in project.godot). Owns the "in dialogue" game mode:
// shows one DialogueLine at a time in a bottom-of-screen box, advances on
// Interact, and presents choices as focusable buttons. It opens as a window, so
// the released mouse and the frozen gameplay underneath both follow from
// UiWindowManager rather than being arranged here.
//
// The UI is built in code so this stays a single-file stub; it gets replaced
// wholesale by the Yarn Spinner dialogue box (docs/plans/npc-dialogue-yarn.md).
public partial class DialogueManager : CanvasLayer, IUiWindow
{
    public static DialogueManager Instance { get; private set; }

    public static bool IsDialogueActive => Instance?.current != null;

    private DialogueLine current;
    private Action onEnded;

    private Label speakerLabel;
    private Label textLabel;
    private VBoxContainer choicesBox;
    private Label continueHint;

    public override void _Ready()
    {
        Instance = this;
        Layer = UiLayers.Dialogue;
        BuildUi();
        Visible = false;
    }

    public string WindowName => "dialogue";

    // A conversation wants the screen: this is what hides the dialogue editor
    // and the console behind a "play from here" preview, and restores them
    // when the conversation ends.
    public bool Exclusive => true;

    // Conversations are advanced, not cancelled. Escape is still swallowed
    // rather than opening the pause menu behind the dialogue box.
    public bool ClosesOnCancel => false;

    public void SetShown(bool shown) => Visible = shown;

    public void OnClosed()
    {
        current = null;
        var ended = onEnded;
        onEnded = null;
        ended?.Invoke();
    }

    // Starts a conversation. onEnded runs after the last line, whichever
    // branch the player takes.
    public void Start(DialogueLine first, Action onEnded = null)
    {
        if (current != null)
        {
            GD.PushWarning("DialogueManager.Start called while a dialogue is already active; ignoring.");
            return;
        }
        this.onEnded = onEnded;
        // Opening releases the mouse so choices are clickable; keyboard works too.
        UiWindowManager.Open(this);
        ShowLine(first);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (current == null || current.Choices is { Count: > 0 })
        {
            return;
        }
        if (@event.IsActionPressed("Interact") || @event.IsActionPressed("ui_accept"))
        {
            GetViewport().SetInputAsHandled();
            Advance(current.Next);
        }
    }

    private void ShowLine(DialogueLine line)
    {
        current = line;
        speakerLabel.Text = line.Speaker ?? string.Empty;
        speakerLabel.Visible = !string.IsNullOrEmpty(line.Speaker);
        textLabel.Text = line.Text;

        foreach (var child in choicesBox.GetChildren())
        {
            child.QueueFree();
        }
        var hasChoices = line.Choices is { Count: > 0 };
        continueHint.Visible = !hasChoices;
        if (hasChoices)
        {
            Button first = null;
            foreach (var choice in line.Choices)
            {
                var captured = choice;
                var button = new Button { Text = choice.Label };
                button.Pressed += () => PickChoice(captured);
                choicesBox.AddChild(button);
                first ??= button;
            }
            // Focus after the button enters the tree so keyboard selection
            // (arrows + Enter) works without touching the mouse.
            first?.CallDeferred(Control.MethodName.GrabFocus);
        }

        line.OnShown?.Invoke();
    }

    private void PickChoice(DialogueChoice choice)
    {
        choice.Action?.Invoke();
        Advance(choice.Next);
    }

    private void Advance(DialogueLine next)
    {
        if (next != null)
        {
            ShowLine(next);
        }
        else
        {
            End();
        }
    }

    // Teardown lives in OnClosed so it runs no matter what closed the window.
    private void End() => UiWindowManager.Close(this);

    private void BuildUi()
    {
        var panel = new PanelContainer
        {
            AnchorLeft = 0.15f,
            AnchorRight = 0.85f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetTop = -230,
            OffsetBottom = -30,
            GrowVertical = Control.GrowDirection.Begin,
        };
        AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 20);
        margin.AddThemeConstantOverride("margin_right", 20);
        margin.AddThemeConstantOverride("margin_top", 14);
        margin.AddThemeConstantOverride("margin_bottom", 14);
        panel.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        margin.AddChild(column);

        speakerLabel = new Label();
        speakerLabel.AddThemeFontSizeOverride("font_size", 24);
        speakerLabel.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f));
        column.AddChild(speakerLabel);

        textLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        textLabel.AddThemeFontSizeOverride("font_size", 20);
        column.AddChild(textLabel);

        choicesBox = new VBoxContainer();
        choicesBox.AddThemeConstantOverride("separation", 4);
        column.AddChild(choicesBox);

        continueHint = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Modulate = new Color(1, 1, 1, 0.6f),
        };
        continueHint.AddThemeFontSizeOverride("font_size", 16);
        continueHint.Text = $"[{InteractionPrompt.FirstBindingLabel("Interact")}] Continue";
        column.AddChild(continueHint);
    }
}
