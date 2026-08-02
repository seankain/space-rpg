using Godot;

// Autoload (registered in project.godot). The one place that knows what is on
// screen: every window opens and closes through here, and the three things that
// used to be recomputed at each closer — visibility, the pointer, and whether
// gameplay is frozen — are derived from the stack instead.
//
// The rules themselves live in the engine-free UiWindowStack; this is the Godot
// half that owns the singleton, applies Input.MouseMode, and turns Escape into
// "close the top window".
public partial class UiWindowManager : Node
{
    public static UiWindowManager Instance { get; private set; }

    private readonly UiWindowStack stack = new();

    // The single flag gameplay checks — Player movement, the Pickup/Npc/Portal/
    // Door interactors, and the camera. Replaces the copy-pasted
    // "IsDialogueActive || IsShopOpen || DevConsole.BlocksGameplay" predicate,
    // and covers the menus, which that predicate never did.
    public static bool BlocksGameplay => Instance?.stack.BlocksGameplay ?? false;

    public static bool AnyOpen => Instance?.stack.AnyOpen ?? false;

    public static IUiWindow Top => Instance?.stack.Top;

    public static bool IsOpen(IUiWindow window) => Instance?.stack.IsOpen(window) ?? false;

    // Bottom to top, for the console's `list windows` — the answer to "what is
    // actually up right now, and which of them is holding the pointer".
    public static System.Collections.Generic.IReadOnlyList<IUiWindow> OpenWindows =>
        Instance?.stack.Open ?? System.Array.Empty<IUiWindow>();

    public override void _Ready()
    {
        Instance = this;
        // Battles pause the tree; Escape and window bookkeeping still have to
        // work while they do (EventToasts runs Always for the same reason).
        ProcessMode = ProcessModeEnum.Always;
        stack.Changed += ApplyPointer;
    }

    public static void Open(IUiWindow window) => Instance?.stack.Push(window);

    public static void Close(IUiWindow window) => Instance?.stack.Close(window);

    public static bool CloseTop() => Instance?.stack.CloseTop() ?? false;

    public static void CloseAll() => Instance?.stack.CloseAll();

    // The only Escape handler in the game. Windows used to run their own, at
    // three different priorities and in two different spellings (raw Key.Escape
    // vs the ui_cancel action), which is why some windows opened over each
    // other and NewGameMenu had no exit at all.
    //
    // Handled in _Input rather than _UnhandledInput so a focused field inside a
    // window can't swallow the cancel — matching where the save/load menus used
    // to do it. Anything still open consumes the key even when it declines to
    // close (a conversation, a battle), so Escape can never reach past a window
    // to the pause menu underneath.
    public override void _Input(InputEvent @event)
    {
        if (!@event.IsActionPressed("ui_cancel") || !stack.AnyOpen)
        {
            return;
        }
        GetViewport().SetInputAsHandled();
        stack.CloseTop();
    }

    // Written once per stack change, never per frame, so the editor fly
    // camera's hold-to-look capture is left alone between changes.
    private void ApplyPointer()
    {
        Input.MouseMode = stack.ReleasesPointer
            ? Input.MouseModeEnum.Visible
            : Input.MouseModeEnum.Captured;
    }
}
