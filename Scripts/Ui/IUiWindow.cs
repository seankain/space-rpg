// One openable UI surface, described by policy rather than by the system that
// happens to own it. Engine-free on purpose: UiWindowStack reasons about these
// without touching Godot, so the open/close/pointer rules are testable headless
// (Tests/UiWindowStackTests.cs). The Godot side is the SetShown implementation,
// which every window node fills in with `Visible = shown`.
//
// The defaults describe the common case — a full-screen menu that takes the
// pointer, freezes gameplay, stacks over whatever is beneath it, and closes on
// Escape. A window only states what differs.
public interface IUiWindow
{
    // Diagnostic label, shown by the console's `list windows`; never shown to
    // players.
    string WindowName { get; }

    // Whether the pointer should be free while this is open. UiWindowManager
    // derives Input.MouseMode from every open window at once, so no window sets
    // the mouse mode itself.
    bool ReleasesPointer => true;

    // Whether normal play (movement, interaction, camera look) is frozen while
    // this is open. Aggregated into UiWindowManager.BlocksGameplay, which is the
    // single flag gameplay code checks.
    bool BlocksGameplay => true;

    // Whether this hides the windows underneath it. Exclusive windows want the
    // whole screen (the NPC editor and the dialogue editor share a layer and
    // both do); non-exclusive ones layer over what is already there.
    bool Exclusive => false;

    // Whether Escape closes this. False for windows that own their own exit —
    // a conversation advances rather than cancels, editor mode ends with the
    // `editor` command, a battle ends by being fought. Escape is still consumed
    // rather than falling through to the pause menu.
    bool ClosesOnCancel => true;

    // Veto hook, checked before Escape closes this window. Returning false
    // keeps it open, which is how a window with an inner step backs out one
    // level instead (MainMenu returns to its buttons from a sub-panel).
    bool RequestClose() => true;

    // Called by the stack whenever this window's effective visibility changes —
    // opened, closed, covered by an exclusive window, or uncovered again.
    // Windows must not set their own visibility outside this.
    void SetShown(bool shown);

    // Called when this window leaves the stack for good, as opposed to merely
    // being covered. Where a window drops the state it only holds while open —
    // the shop's merchant, the conversation's callback — so that state can't
    // survive a close it didn't initiate itself (Escape, or a CloseAll).
    void OnClosed() { }
}
