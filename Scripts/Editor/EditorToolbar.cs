using System;
using System.Collections.Generic;
using Godot;

// What a left-click in the level does while editor mode is active.
public enum EditorTool
{
    // Fly and look only — clicks in the world do nothing.
    Navigate,

    // Click a spot on the ground to drop a new NPC there and open its editor.
    PlaceNpc,

    // Click an existing NPC to open its editor.
    EditNpc,
}

// The editor-mode tool palette: a strip of buttons down the right-hand side of
// the screen, alive only while editor mode is on. Picking a tool decides what
// a click in the level means — DevConsole reads ActiveTool when the world is
// clicked and runs the matching action.
//
// Code-built CanvasLayer like the rest of the in-game editor UI. It owns no
// editor state beyond the current tool: placement, editing, and saving all
// live behind the callbacks DevConsole hands it.
public partial class EditorToolbar : CanvasLayer
{
    // Raised when the author asks to leave editor mode.
    public Action ExitRequested { get; set; }

    // Raised when the tool changes, so the hosting console can clear anything
    // the previous tool had half-finished.
    public Action<EditorTool> ToolChanged { get; set; }

    public EditorTool ActiveTool { get; private set; } = EditorTool.Navigate;

    private static readonly Color HeadingColor = new(0.4f, 1f, 0.6f);
    private static readonly Color MutedColor = new(0.65f, 0.67f, 0.7f);
    private static readonly Color ActiveColor = new(1f, 0.85f, 0.4f);

    private readonly Dictionary<EditorTool, Button> toolButtons = new();
    private Label hintLabel;
    private Label statusLabel;

    private static readonly (EditorTool Tool, string Label, string Hint)[] Tools =
    {
        (EditorTool.Navigate, "Navigate",
            "WASD flies, Space/Ctrl rise and sink, Shift boosts.\nHold right-mouse to look; wheel retunes speed."),
        (EditorTool.PlaceNpc, "Place NPC",
            "Click a spot in the level to drop a new NPC there.\nThe NPC editor opens on it; Save writes its .tres."),
        (EditorTool.EditNpc, "Edit NPC",
            "Click an NPC to edit its rig, stats, items and credits."),
    };

    public override void _Ready()
    {
        Layer = UiLayers.EditorToolbar;
        BuildUi();
        SelectTool(EditorTool.Navigate);
    }

    // Shown under the tool buttons: what the last click did, or why it didn't.
    public void SetStatus(string text, bool ok)
    {
        statusLabel.Text = text ?? "";
        statusLabel.AddThemeColorOverride(
            "font_color", ok ? MutedColor : new Color(1f, 0.5f, 0.45f));
    }

    public void SelectTool(EditorTool tool)
    {
        ActiveTool = tool;
        foreach (var (candidate, button) in toolButtons)
        {
            var active = candidate == tool;
            button.ButtonPressed = active;
            button.AddThemeColorOverride("font_color", active ? ActiveColor : Colors.White);
        }
        foreach (var entry in Tools)
        {
            if (entry.Tool == tool)
            {
                hintLabel.Text = entry.Hint;
            }
        }
        ToolChanged?.Invoke(tool);
    }

    private void BuildUi()
    {
        var panel = new PanelContainer
        {
            // Anchored to the right edge, clear of the console panel (which
            // covers the top 45%) and the bottom-left HUD strip.
            AnchorLeft = 1f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 0f,
            OffsetLeft = -232,
            OffsetRight = -12,
            OffsetTop = 12,
            // Height comes from the content: a Control never shrinks below its
            // combined minimum size, so the strip is exactly as tall as the
            // buttons and hints inside it and grows downward.
            OffsetBottom = 12,
            GrowHorizontal = Control.GrowDirection.Begin,
            GrowVertical = Control.GrowDirection.End,
        };
        panel.AddThemeStyleboxOverride(
            "panel", new StyleBoxFlat { BgColor = new Color(0.06f, 0.07f, 0.09f, 0.92f) });
        AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        panel.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        margin.AddChild(column);

        var heading = new Label { Text = "EDITOR" };
        heading.AddThemeColorOverride("font_color", HeadingColor);
        heading.AddThemeFontSizeOverride("font_size", 18);
        column.AddChild(heading);

        column.AddChild(new HSeparator());

        foreach (var (tool, label, _) in Tools)
        {
            var button = new Button
            {
                Text = label,
                ToggleMode = true,
                Alignment = HorizontalAlignment.Left,
                // The palette is the one place a click must never fall through
                // to the level, so let the buttons take focus normally but keep
                // the keyboard on the camera by releasing it right after.
                FocusMode = Control.FocusModeEnum.None,
            };
            var picked = tool;
            button.Pressed += () => SelectTool(picked);
            column.AddChild(button);
            toolButtons[tool] = button;
        }

        column.AddChild(new HSeparator());

        hintLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(210, 0),
        };
        hintLabel.AddThemeColorOverride("font_color", MutedColor);
        hintLabel.AddThemeFontSizeOverride("font_size", 13);
        column.AddChild(hintLabel);

        statusLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(210, 0),
        };
        statusLabel.AddThemeColorOverride("font_color", MutedColor);
        statusLabel.AddThemeFontSizeOverride("font_size", 13);
        column.AddChild(statusLabel);

        column.AddChild(new HSeparator());

        var console = new Label { Text = "~  console" };
        console.AddThemeColorOverride("font_color", MutedColor);
        console.AddThemeFontSizeOverride("font_size", 13);
        column.AddChild(console);

        var exit = new Button { Text = "Exit editor mode", FocusMode = Control.FocusModeEnum.None };
        exit.Pressed += () => ExitRequested?.Invoke();
        column.AddChild(exit);
    }
}
