using System;
using System.Collections.Generic;
using System.Linq;
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

    // True while the console panel is showing.
    public static bool IsOpen => Instance is { enabled: true, Visible: true };

    // True while editor mode is active (the `editor` command). Orthogonal to
    // the console being open.
    public static bool IsEditorActive => Instance is { enabled: true, editorActive: true };

    // True while the read-only dialogue viewer (Phase 3) is showing.
    public static bool IsDialogueViewerOpen => Instance?.dialogueViewer is { Visible: true };

    // The single flag gameplay checks to freeze normal play — true while the
    // console is open, editor mode is active, or the dialogue viewer is up.
    // Guarded cooperatively, the same way Player/Pickup/Npc check
    // DialogueManager.IsDialogueActive.
    public static bool BlocksGameplay => IsOpen || IsEditorActive || IsDialogueViewerOpen;

    // The world point the placement marker sits on, or null when editor mode is
    // off or the ray misses. The `here` token (Phase 3) resolves to this.
    public static Vector3? PlacementCursorPosition =>
        IsEditorActive ? Instance.cursor?.WorldPosition : null;

    private static readonly Color EchoColor = new(0.6f, 0.75f, 0.95f);
    private static readonly Color OkColor = new(0.8f, 0.85f, 0.78f);
    private static readonly Color ErrorColor = new(1f, 0.5f, 0.45f);
    private static readonly Color HintColor = new(0.6f, 0.62f, 0.66f);

    // Submitted commands, oldest first, walked with Up/Down. historyIndex points
    // at the entry shown; history.Count means the (empty) live draft.
    private readonly List<string> history = new();
    private int historyIndex;

    // Editor tooling must never reach a shipped build; when false the toggle is
    // inert and the panel never shows.
    private bool enabled;

    private readonly CommandRegistry registry = new();

    private RichTextLabel output;
    private LineEdit input;

    // Editor-mode state: a translucent placement marker parented under the
    // scene root and a bottom-left HUD strip, both alive only while editor mode
    // is on.
    private bool editorActive;
    private EditorCursor cursor;
    private Label editorHud;

    // The read-only dialogue viewer (Phase 3), created on first `dialogue open`
    // and reused after; null until then, hidden when closed.
    private DialogueEditorPanel dialogueViewer;

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
        registry.Register(new EditorModeCommand(this));
        registry.Register(new EditorModeCommand(this, "edit"));
        // Phase 3: NPC placement and authoring.
        registry.Register(new SpawnCommand());
        registry.Register(new PlaceCommand());
        registry.Register(new SaveNpcCommand());
        registry.Register(new NudgeCommand());
        registry.Register(new RotateCommand());
        registry.Register(new CancelPlaceCommand());
        registry.Register(new ListCommand());
        // Phase 4: items and credits.
        registry.Register(new GiveCommand());
        registry.Register(new GiveItemCommand());
        registry.Register(new TakeItemCommand());
        registry.Register(new CreditsCommand());
        registry.Register(new ItemsCommand());
        // Phase 5: quests.
        registry.Register(new QuestCommand());
        registry.Register(new QuestsCommand());
        // World flags — dialogue state with no UI of its own
        // (npc-dialogue-yarn.md Phase 3).
        registry.Register(new FlagCommand());
        registry.Register(new FlagsCommand());
        // Phase 6: convenience.
        registry.Register(new SaveCommand());
        registry.Register(new GotoCommand(this));
        registry.Register(new ExecCommand(this));
        // Dialogue editor (dialogue-editor plan): read-only viewer.
        registry.Register(new DialogueCommand(this));
        Print("Developer console. Type 'help' for commands.", OkColor);
    }

    // Opens the dialogue editor on a graph by id (Phase 3-4), editing a working
    // clone so edits don't touch the cached catalog graph until save. Called by
    // DialogueCommand; returns a result the console prints.
    public CommandResult OpenDialogueViewer(string id)
    {
        if (!enabled)
        {
            return CommandResult.Fail("Editor tooling is disabled in this build.");
        }
        var graph = DialogueCatalog.Get(id);
        if (graph == null)
        {
            return CommandResult.Fail($"No dialogue '{id}'. Try 'dialogue list'.");
        }
        ShowDialogueViewer(DialogueGraphEditing.Clone(graph));
        return CommandResult.Ok(
            $"Editing '{id}'. Toggle the console (~) to see the panel; Save writes it; Esc or 'dialogue close' exits.");
    }

    // Seeds a fresh single-node conversation and opens it for editing (Phase 4).
    public CommandResult NewDialogue(string id)
    {
        if (!enabled)
        {
            return CommandResult.Fail("Editor tooling is disabled in this build.");
        }
        if (string.IsNullOrEmpty(id))
        {
            return CommandResult.Fail("Usage: dialogue new <id>");
        }
        if (DialogueCatalog.Get(id) != null)
        {
            return CommandResult.Fail($"'{id}' already exists. Open it with 'dialogue open {id}'.");
        }
        ShowDialogueViewer(DialogueGraphEditing.NewEmpty(id));
        return CommandResult.Ok($"New dialogue '{id}'. Edit it, then 'dialogue save' (or the Save button) to write it.");
    }

    // Validates and writes the editor's working graph (Phase 4). An optional id
    // renames it (a "save as" to a new file).
    public CommandResult SaveDialogue(string idOverride)
    {
        if (dialogueViewer?.WorkingGraph == null)
        {
            return CommandResult.Fail("No dialogue open. Use 'dialogue open <id>' or 'dialogue new <id>' first.");
        }
        var graph = dialogueViewer.WorkingGraph;
        if (!string.IsNullOrEmpty(idOverride))
        {
            graph.Id = idOverride;
        }
        var result = DialogueEditing.Save(graph);
        if (result.Success)
        {
            // Reflect a possible rename and the now-cataloged id, keeping the
            // author's current node selection.
            dialogueViewer.OnSaved();
            // Persist the graph-canvas layout beside it, if the author used the
            // canvas (best-effort — never fails the graph save).
            if (dialogueViewer.CaptureLayout() is { } layout)
            {
                DialogueEditing.SaveLayout(layout);
            }
        }
        dialogueViewer.SetStatus(result.Message, result.Success);
        return result;
    }

    private void ShowDialogueViewer(DialogueGraph workingGraph)
    {
        EnsureDialogueViewer();
        dialogueViewer.ShowGraph(workingGraph);
        dialogueViewer.Visible = true;
        // Keep the pointer free so the editor stays usable even after the
        // console is toggled shut to see the panel in full.
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private void EnsureDialogueViewer()
    {
        if (dialogueViewer != null)
        {
            return;
        }
        dialogueViewer = new DialogueEditorPanel
        {
            Closed = () => CloseDialogueViewer(),
            OpenRequested = id =>
            {
                var result = OpenDialogueViewer(id);
                if (!result.Success)
                {
                    dialogueViewer.SetStatus(result.Message, false);
                }
            },
            SaveRequested = () =>
            {
                var result = SaveDialogue(null);
                Print(result.Message, result.Success ? OkColor : ErrorColor);
            },
            PlayRequested = startNodeId =>
            {
                var result = PlayDialoguePreview(startNodeId);
                if (!result.Success)
                {
                    dialogueViewer.SetStatus(result.Message, false);
                }
            },
        };
        AddChild(dialogueViewer);
    }

    // "Play from here" (Phase 5): compiles the editor's working (unsaved) graph
    // from a node and plays it through DialogueManager against the running
    // GameState. Pure effects fire against that live state; scene effects are
    // logged, not executed (DialoguePreviewHost). The editor UI hides while the
    // dialogue box plays and returns when it ends.
    public CommandResult PlayDialoguePreview(string startNodeId)
    {
        if (dialogueViewer?.WorkingGraph == null)
        {
            return CommandResult.Fail("No dialogue open. Use 'dialogue open <id>' or 'dialogue new <id>' first.");
        }
        var state = SaveManager.Instance?.CurrentState;
        if (state == null)
        {
            return CommandResult.Fail("Preview needs a running game — load or start one first.");
        }
        if (DialogueManager.Instance == null || DialogueManager.IsDialogueActive)
        {
            return CommandResult.Fail("A conversation is already playing.");
        }
        var graph = dialogueViewer.WorkingGraph;
        startNodeId ??= dialogueViewer.SelectedNodeId;
        var context = new DialogueContext
        {
            State = state,
            SpeakerName = graph.Id,
            Host = new DialoguePreviewHost(message => Print(message, HintColor)),
            LogWarning = message => Print(message, ErrorColor),
        };
        var line = DialogueRuntime.Compile(graph, context, startNodeId);
        if (line == null)
        {
            return CommandResult.Fail($"Nothing to play from '{startNodeId ?? graph.EntryNodeId}'.");
        }

        // Hide the editor UI (and close the console) so the dialogue box shows
        // uncovered; DialogueManager blocks gameplay while it plays.
        dialogueViewer.Visible = false;
        if (Visible)
        {
            Toggle();
        }
        DialogueManager.Instance.Start(line, () =>
        {
            dialogueViewer.Visible = true;
            Input.MouseMode = Input.MouseModeEnum.Visible;
        });
        return CommandResult.Ok($"Playing '{graph.Id}' from '{startNodeId ?? graph.EntryNodeId}'. (preview — scene effects are skipped)");
    }

    // Hides the dialogue viewer and restores the pointer (visible if the
    // console is still open, captured back to gameplay otherwise). Returns
    // whether it was open.
    public bool CloseDialogueViewer()
    {
        if (dialogueViewer is not { Visible: true })
        {
            return false;
        }
        dialogueViewer.Visible = false;
        Input.MouseMode = Visible ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
        return true;
    }

    // Flips editor mode and returns the new state; called by EditorModeCommand.
    public bool ToggleEditorMode()
    {
        if (!enabled)
        {
            return false;
        }
        SetEditorMode(!editorActive);
        return editorActive;
    }

    private void SetEditorMode(bool active)
    {
        if (active == editorActive)
        {
            return;
        }
        editorActive = active;
        editorHud.Visible = active;
        if (active)
        {
            // Parent the marker under the scene root so it lives in the main
            // 3D world and survives level changes; freed when editor mode ends.
            cursor = new EditorCursor();
            GetTree().Root.AddChild(cursor);
        }
        else
        {
            cursor?.QueueFree();
            cursor = null;
        }
    }

    public override void _Process(double delta)
    {
        KeepInputFocused();
        if (!editorActive)
        {
            return;
        }
        if (cursor?.WorldPosition is { } p)
        {
            var chunk = ChunkManager.ToChunkCoord(p);
            editorHud.Text =
                $"EDITOR MODE\ncursor  ({p.X:0.0}, {p.Y:0.0}, {p.Z:0.0})   chunk ({chunk.X}, {chunk.Y})";
        }
        else
        {
            editorHud.Text = "EDITOR MODE\ncursor  (no target)";
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (!enabled)
        {
            return;
        }
        if (@event.IsActionPressed("ToggleConsole"))
        {
            // Handle the toggle in _Input (ahead of GUI input) so the tilde
            // keystroke never leaks into the LineEdit as a stray character.
            Toggle();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (!Visible || @event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }
        // While open, own Tab (completion) and Up/Down (history) ahead of GUI
        // input so the LineEdit doesn't traverse focus or the Inventory action
        // doesn't fire. A single-line LineEdit ignores Up/Down anyway.
        if (Matches(key, Key.Tab))
        {
            CompleteInput();
            GetViewport().SetInputAsHandled();
        }
        else if (Matches(key, Key.Up))
        {
            NavigateHistory(-1);
            GetViewport().SetInputAsHandled();
        }
        else if (Matches(key, Key.Down))
        {
            NavigateHistory(1);
            GetViewport().SetInputAsHandled();
        }
    }

    // Backstop for the typing-goes-nowhere case: KeepEditingOnTextSubmit covers
    // Enter, but a click on the log or Escape also ends edit mode, and either
    // one leaves the console looking ready while ignoring the keyboard. While
    // the panel is open the command line owns input, so put it back in edit
    // mode (Edit also takes focus). Skipped while the dialogue editor is up so
    // its own fields keep the caret.
    private void KeepInputFocused()
    {
        if (!enabled || !Visible || IsDialogueViewerOpen)
        {
            return;
        }
        if (!input.IsEditing())
        {
            input.Edit();
        }
    }

    private static bool Matches(InputEventKey key, Key which) =>
        key.Keycode == which || key.PhysicalKeycode == which;

    private void NavigateHistory(int direction)
    {
        if (history.Count == 0)
        {
            return;
        }
        historyIndex = Math.Clamp(historyIndex + direction, 0, history.Count);
        input.Text = historyIndex < history.Count ? history[historyIndex] : "";
        input.CaretColumn = input.Text.Length;
    }

    private void CompleteInput()
    {
        var completion = CommandCompleter.CompleteLine(input.Text, CompletionCandidates(input.Text));
        input.Text = completion.Line;
        input.CaretColumn = input.Text.Length;
        if (completion.Matches.Count > 1)
        {
            Print("  " + string.Join("   ", completion.Matches), HintColor);
        }
    }

    // Candidates for the token currently being typed: command names for the
    // first token, then per-command ids (items, quests, npcs) for known verbs.
    private IReadOnlyList<string> CompletionCandidates(string line)
    {
        var tokens = CommandTokenizer.Tokenize(line);
        var trailingSpace = line.Length > 0 && char.IsWhiteSpace(line[^1]);
        var index = trailingSpace ? tokens.Count : Math.Max(0, tokens.Count - 1);
        if (index == 0)
        {
            return registry.All.Select(c => c.Name).ToList();
        }
        var command = tokens.Count > 0 ? tokens[0].ToLowerInvariant() : "";
        return command switch
        {
            "give" or "takeitem" when index == 1 => ItemCatalog.All.Select(i => i.Id.ToString()).ToList(),
            "quest" when index == 1 => new List<string> { "start", "set", "stage", "advance" },
            "quest" when index == 2 => QuestCatalog.All.Select(q => q.Id.ToString()).ToList(),
            "flag" when index == 1 => new List<string> { "set", "clear", "get" },
            // Only flags that exist can be completed; a new one is typed out.
            "flag" when index == 2 => SaveManager.Instance?.CurrentState?.Flags.Keys.ToList()
                ?? new List<string>(),
            "spawn" or "goto" when index == 1 => NpcDatabase.All.Select(d => d.NpcId).ToList(),
            "list" when index == 1 => new List<string> { "npcs" },
            "dialogue" when index == 1 => new List<string> { "list", "open", "new", "save", "play", "close", "assign" },
            "dialogue" when index == 2 && tokens.Count > 1 && tokens[1].ToLowerInvariant() == "assign"
                => NpcDatabase.All.Select(d => d.NpcId).ToList(),
            "dialogue" when index == 2 => DialogueCatalog.Ids.ToList(),
            "dialogue" when index == 3 && tokens.Count > 1 && tokens[1].ToLowerInvariant() == "assign"
                => DialogueCatalog.Ids.ToList(),
            _ => new List<string>(),
        };
    }

    private void Toggle()
    {
        Visible = !Visible;
        if (Visible)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            input.Clear();
            // Edit rather than GrabFocus: focus alone doesn't put a 4.4+
            // LineEdit in edit mode, and the console should be typeable the
            // moment it drops down.
            input.Edit();
            // Anything printed while the panel was hidden laid out just now, so
            // open on the newest line rather than wherever the log was left.
            ScrollOutputToBottom();
        }
        else
        {
            // Don't recapture the pointer out from under an open viewer panel.
            Input.MouseMode = IsDialogueViewerOpen
                ? Input.MouseModeEnum.Visible
                : Input.MouseModeEnum.Captured;
        }
    }

    private void OnCommandSubmitted(string text)
    {
        // Keep the input ready for the next command regardless of outcome; the
        // caret stays put (KeepEditingOnTextSubmit), and commands that close the
        // console — `dialogue play` — leave it closed rather than pulling the
        // keyboard back to a hidden field.
        input.Clear();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        if (history.Count == 0 || history[^1] != text)
        {
            history.Add(text);
        }
        historyIndex = history.Count;
        ExecuteLine(text);
    }

    // Echo a command, dispatch it, and print the result. Shared by interactive
    // submission and exec scripts.
    public void ExecuteLine(string line)
    {
        if (!enabled || string.IsNullOrWhiteSpace(line))
        {
            return;
        }
        Print($"] {line}", EchoColor);
        var result = registry.Dispatch(line);
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

    // Runs a newline-delimited script: blank lines and #-comments are skipped.
    // Returns how many commands ran (the ExecCommand reports the count).
    public int ExecuteScript(string text)
    {
        var count = 0;
        foreach (var raw in (text ?? "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }
            ExecuteLine(line);
            count++;
        }
        return count;
    }

    private void Print(string text, Color color)
    {
        output.PushColor(color);
        output.AddText(text);
        output.Pop();
        output.Newline();
        ScrollOutputToBottom();
    }

    // ScrollFollowing alone isn't enough: RichTextLabel only keeps following
    // while the view is already pinned to the end, so scrolling back through
    // the log (or printing while the panel is hidden) strands the newest lines
    // off-screen. Pin the view to the last line after every print — deferred,
    // because the text added above only re-flows at the end of the frame.
    private void ScrollOutputToBottom() => Callable.From(ScrollOutputToEnd).CallDeferred();

    private void ScrollOutputToEnd()
    {
        // GetLineCount forces the pending re-flow, so both the line index and
        // the scrollbar range are current by the time we scroll.
        var lines = output.GetLineCount();
        if (lines > 0)
        {
            output.ScrollToLine(lines - 1);
        }
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
            // Godot 4.4+ separates focus from edit mode: by default a LineEdit
            // leaves edit mode when ui_text_submit fires, so after Enter the
            // field still holds focus but ignores every keystroke — the user
            // had to click back in for each command. Stay in edit mode instead.
            KeepEditingOnTextSubmit = true,
        };
        input.TextSubmitted += OnCommandSubmitted;
        column.AddChild(input);

        // Editor-mode HUD strip, anchored bottom-left so it clears the console
        // panel (top) and the dialogue box (bottom-center). Outlined for
        // readability over arbitrary 3D backgrounds; hidden until editor mode.
        editorHud = new Label
        {
            Text = "EDITOR MODE",
            Visible = false,
            AnchorLeft = 0f,
            AnchorRight = 0f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetLeft = 16,
            OffsetRight = 460,
            OffsetTop = -84,
            OffsetBottom = -36,
            GrowVertical = Control.GrowDirection.Begin,
        };
        editorHud.AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.6f));
        editorHud.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        editorHud.AddThemeConstantOverride("outline_size", 4);
        editorHud.AddThemeFontSizeOverride("font_size", 16);
        AddChild(editorHud);
    }
}
