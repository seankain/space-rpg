using System;
using System.Collections.Generic;

// The ordered set of open UI windows, bottom to top, and the rules that follow
// from it: which windows are visible, whether the pointer is free, and whether
// gameplay is frozen. Engine-free so the rules can be tested headless; the
// Godot half is UiWindowManager, which owns the singleton and applies the
// pointer.
//
// This replaces the per-window conditional chains the systems used to carry
// ("captured unless the console is open, or editor mode is on, or…"): every
// question is answered once, from the stack, rather than once per closer.
public sealed class UiWindowStack
{
    private readonly List<IUiWindow> open = new();

    // Raised after any change settles, so the host can apply the pointer once
    // per change rather than once per window touched.
    public event Action Changed;

    // Bottom to top. The last entry is the window Escape acts on.
    public IReadOnlyList<IUiWindow> Open => open;

    public IUiWindow Top => open.Count > 0 ? open[^1] : null;

    public bool AnyOpen => open.Count > 0;

    public bool IsOpen(IUiWindow window) => window != null && open.Contains(window);

    // Aggregated over every open window, covered ones included: a window hidden
    // behind an exclusive one is still open, and whatever covered it is at
    // least as restrictive.
    public bool ReleasesPointer => AnyWindow(static w => w.ReleasesPointer);

    public bool BlocksGameplay => AnyWindow(static w => w.BlocksGameplay);

    private bool AnyWindow(Func<IUiWindow, bool> predicate)
    {
        foreach (var window in open)
        {
            if (predicate(window))
            {
                return true;
            }
        }
        return false;
    }

    // Opens a window, or raises an already-open one to the top. Raising rather
    // than rejecting keeps re-entry cheap: a system that reopens its own panel
    // doesn't have to check first.
    public void Push(IUiWindow window)
    {
        if (window == null)
        {
            return;
        }
        var at = open.IndexOf(window);
        if (at == open.Count - 1 && at >= 0)
        {
            // Already on top; nothing to settle.
            return;
        }
        if (at >= 0)
        {
            open.RemoveAt(at);
        }
        open.Add(window);
        Settle();
    }

    // Closes a window wherever it sits in the stack, not just on top. Returns
    // whether it was open.
    public bool Close(IUiWindow window)
    {
        if (window == null || !open.Remove(window))
        {
            return false;
        }
        window.SetShown(false);
        Settle();
        // After the stack has settled, so a window that opens another one as it
        // tears down (a conversation handing off to a shop) sees a consistent
        // stack rather than one mid-mutation.
        window.OnClosed();
        return true;
    }

    // What Escape means. Returns whether a window actually closed — false when
    // the stack is empty, when the top window doesn't close on cancel, or when
    // it vetoed the close (having handled the Escape some other way).
    public bool CloseTop()
    {
        var top = Top;
        if (top == null || !top.ClosesOnCancel || !top.RequestClose())
        {
            return false;
        }
        return Close(top);
    }

    // Drops every window without consulting ClosesOnCancel or RequestClose —
    // for hard transitions that invalidate the whole UI, like the game-over
    // teardown that discards the running level.
    public void CloseAll()
    {
        if (open.Count == 0)
        {
            return;
        }
        // Top down, so a window never briefly re-shows as the one above it goes.
        var closed = open.ToArray();
        for (var i = closed.Length - 1; i >= 0; i--)
        {
            closed[i].SetShown(false);
        }
        open.Clear();
        Settle();
        for (var i = closed.Length - 1; i >= 0; i--)
        {
            closed[i].OnClosed();
        }
    }

    // Applies the visibility rule and announces the change. Everything from the
    // topmost exclusive window up is shown; everything below it is hidden.
    private void Settle()
    {
        var firstShown = 0;
        for (var i = open.Count - 1; i >= 0; i--)
        {
            if (open[i].Exclusive)
            {
                firstShown = i;
                break;
            }
        }
        for (var i = 0; i < open.Count; i++)
        {
            open[i].SetShown(i >= firstShown);
        }
        Changed?.Invoke();
    }
}
