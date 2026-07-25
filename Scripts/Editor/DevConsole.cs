using Godot;

// Autoload (registered in project.godot). The in-game developer console
// (in-game-editor plan Phase 1): the ToggleConsole action (tilde) drops down a
// panel with a scrollback log and an input line, dispatching typed commands
// through the engine-free CommandRegistry so later phases register verbs
// without touching this UI. Built in code like DialogueManager so it stays a
// single-file node.
//
// Development aid only: a release build never opens it (enabled is gated on a
// debug/editor build), matching the map-baker's editor-only workflow.
public partial class DevConsole : CanvasLayer
{
    public static DevConsole Instance { get; private set; }

    // True while the console panel is showing. Exposed for Phase 2 (editor
    // mode) to suppress gameplay input the way DialogueManager.IsDialogueActive
    // does; nothing consumes it yet.
    public static bool IsOpen => Instance is { enabled: true, Visible: true };

    private static readonly Color EchoColor = new(0.6f, 0.75f, 0.95f);
    private static readonly Color OkColor = new(0.8f, 0.85f, 0.78f);
    private static readonly Color ErrorColor = new(1f, 0.5f, 0.45f);

    // Editor tooling must never reach a shipped build; when false the toggle is
    // inert and the panel never shows.
    private bool enabled;

    private readonly CommandRegistry registry = new();

    private RichTextLabel output;
    private LineEdit input;

    public override void _Ready()
    {
        Instance = this;
        Layer = 100; // above the dialogue box (50) and every menu
        enabled = OS.IsDebugBuild() || OS.HasFeature("editor");
        BuildUi();
        Visible = false;
        if (!enabled)
        {
            return;
        }
        RegisterBuiltins();
    }

    // Feature phases call this to add their verbs (NPC placement, item/quest
    // commands, dialogue editor). No-op registration is safe in a release build.
    public void Register(IConsoleCommand command)
    {
        if (enabled)
        {
            registry.Register(command);
        }
    }

    private void RegisterBuiltins()
    {
        registry.Register(new HelpCommand(registry));
        registry.Register(new ClearCommand());
        registry.Register(new EchoCommand());
        Print("Developer console. Type 'help' for commands.", OkColor);
    }

    public override void _Input(InputEvent @event)
    {
        if (!enabled || !@event.IsActionPressed("ToggleConsole"))
        {
            return;
        }
        // Handle the toggle in _Input (ahead of GUI input) so the tilde
        // keystroke never leaks into the LineEdit as a stray character.
        Toggle();
        GetViewport().SetInputAsHandled();
    }

    private void Toggle()
    {
        Visible = !Visible;
        if (Visible)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            input.Clear();
            input.GrabFocus();
        }
        else
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    private void OnCommandSubmitted(string text)
    {
        // Keep the input ready for the next command regardless of outcome.
        input.Clear();
        input.GrabFocus();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        Print($"] {text}", EchoColor);
        var result = registry.Dispatch(text);
        if (result.ClearOutput)
        {
            output.Clear();
            return;
        }
        if (!string.IsNullOrEmpty(result.Message))
        {
            Print(result.Message, result.Success ? OkColor : ErrorColor);
        }
    }

    private void Print(string text, Color color)
    {
        output.PushColor(color);
        output.AddText(text);
        output.Pop();
        output.Newline();
    }

    private void BuildUi()
    {
        var panel = new PanelContainer
        {
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 0.45f,
        };
        var background = new StyleBoxFlat { BgColor = new Color(0.05f, 0.06f, 0.08f, 0.92f) };
        panel.AddThemeStyleboxOverride("panel", background);
        AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        panel.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        margin.AddChild(column);

        output = new RichTextLabel
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            ScrollFollowing = true,
            SelectionEnabled = true,
            FocusMode = Control.FocusModeEnum.None,
        };
        output.AddThemeFontSizeOverride("normal_font_size", 16);
        column.AddChild(output);

        input = new LineEdit
        {
            PlaceholderText = "Enter command - 'help' for a list",
            ClearButtonEnabled = false,
        };
        input.TextSubmitted += OnCommandSubmitted;
        column.AddChild(input);
    }
}
