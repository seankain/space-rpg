using System.Collections.Generic;
using Xunit;

// The open/close, visibility, pointer and gameplay-blocking rules that used to
// be spread across every window-owning system. These are the behaviours the
// game relies on, stated once against the engine-free stack.
public class UiWindowStackTests
{
    // Stand-in for a window node: records what the stack told it to show.
    private sealed class FakeWindow : IUiWindow
    {
        public FakeWindow(string name) => WindowName = name;

        public string WindowName { get; }
        public bool ReleasesPointer { get; init; } = true;
        public bool BlocksGameplay { get; init; } = true;
        public bool Exclusive { get; init; }
        public bool ClosesOnCancel { get; init; } = true;

        // Set to veto a close, the way MainMenu backs out of a sub-panel.
        public bool RefuseClose { get; set; }
        public int CloseRequests { get; private set; }

        public bool Shown { get; private set; }
        public readonly List<bool> ShownHistory = new();
        public int ClosedNotifications { get; private set; }

        public void OnClosed() => ClosedNotifications++;

        public bool RequestClose()
        {
            CloseRequests++;
            return !RefuseClose;
        }

        public void SetShown(bool shown)
        {
            Shown = shown;
            ShownHistory.Add(shown);
        }
    }

    [Fact]
    public void EmptyStackCapturesPointerAndLeavesGameplayAlone()
    {
        var stack = new UiWindowStack();

        Assert.False(stack.AnyOpen);
        Assert.Null(stack.Top);
        Assert.False(stack.ReleasesPointer);
        Assert.False(stack.BlocksGameplay);
    }

    [Fact]
    public void PushingShowsTheWindowAndReleasesThePointer()
    {
        var stack = new UiWindowStack();
        var menu = new FakeWindow("menu");

        stack.Push(menu);

        Assert.True(menu.Shown);
        Assert.Same(menu, stack.Top);
        Assert.True(stack.ReleasesPointer);
        Assert.True(stack.BlocksGameplay);
    }

    [Fact]
    public void ClosingTheLastWindowRecapturesThePointer()
    {
        var stack = new UiWindowStack();
        var menu = new FakeWindow("menu");
        stack.Push(menu);

        Assert.True(stack.Close(menu));

        Assert.False(menu.Shown);
        Assert.False(stack.AnyOpen);
        Assert.False(stack.ReleasesPointer);
        Assert.False(stack.BlocksGameplay);
    }

    // The bug this refactor exists to kill: closing one window while another is
    // still open used to capture the pointer out from under the survivor.
    [Fact]
    public void ClosingOneWindowKeepsThePointerFreeWhileAnotherIsOpen()
    {
        var stack = new UiWindowStack();
        var pauseMenu = new FakeWindow("pause");
        var inGameMenu = new FakeWindow("in-game");
        stack.Push(pauseMenu);
        stack.Push(inGameMenu);

        stack.Close(inGameMenu);

        Assert.True(stack.ReleasesPointer);
        Assert.True(stack.BlocksGameplay);
        Assert.Same(pauseMenu, stack.Top);
    }

    // A window that doesn't take the pointer (a HUD-ish overlay) must not hold
    // the cursor released on its own.
    [Fact]
    public void PointerFollowsTheWindowsThatActuallyWantIt()
    {
        var stack = new UiWindowStack();
        var passive = new FakeWindow("passive") { ReleasesPointer = false, BlocksGameplay = false };

        stack.Push(passive);

        Assert.False(stack.ReleasesPointer);
        Assert.False(stack.BlocksGameplay);
        Assert.True(stack.AnyOpen);
    }

    [Fact]
    public void ExclusiveWindowHidesWhatIsUnderneathAndRestoresItOnClose()
    {
        var stack = new UiWindowStack();
        var npcEditor = new FakeWindow("npc editor") { Exclusive = true };
        var dialogueEditor = new FakeWindow("dialogue editor") { Exclusive = true };
        stack.Push(npcEditor);

        stack.Push(dialogueEditor);

        Assert.False(npcEditor.Shown);
        Assert.True(dialogueEditor.Shown);

        stack.Close(dialogueEditor);

        Assert.True(npcEditor.Shown);
    }

    // A covered window is still open, so its policy still counts — that is what
    // keeps gameplay frozen underneath a stack of editor panels.
    [Fact]
    public void CoveredWindowStillContributesPolicy()
    {
        var stack = new UiWindowStack();
        var blocking = new FakeWindow("blocking");
        var cover = new FakeWindow("cover") { Exclusive = true, BlocksGameplay = false, ReleasesPointer = false };
        stack.Push(blocking);
        stack.Push(cover);

        Assert.False(blocking.Shown);
        Assert.True(stack.BlocksGameplay);
        Assert.True(stack.ReleasesPointer);
    }

    // Non-exclusive windows layer instead of hiding: the console drops down over
    // whatever is already up.
    [Fact]
    public void NonExclusiveWindowLeavesWhatIsUnderneathVisible()
    {
        var stack = new UiWindowStack();
        var menu = new FakeWindow("menu");
        var console = new FakeWindow("console");
        stack.Push(menu);

        stack.Push(console);

        Assert.True(menu.Shown);
        Assert.True(console.Shown);
    }

    // Opening an exclusive window over an exclusive window over a plain one:
    // only from the topmost exclusive up is visible.
    [Fact]
    public void OnlyTheTopmostExclusiveWindowAndAboveAreShown()
    {
        var stack = new UiWindowStack();
        var console = new FakeWindow("console");
        var editor = new FakeWindow("editor") { Exclusive = true };
        var dialogue = new FakeWindow("dialogue") { Exclusive = true };
        var toastish = new FakeWindow("overlay");

        stack.Push(console);
        stack.Push(editor);
        stack.Push(dialogue);
        stack.Push(toastish);

        Assert.False(console.Shown);
        Assert.False(editor.Shown);
        Assert.True(dialogue.Shown);
        Assert.True(toastish.Shown);
    }

    [Fact]
    public void CloseTopClosesTheTopWindowOnly()
    {
        var stack = new UiWindowStack();
        var lower = new FakeWindow("lower");
        var upper = new FakeWindow("upper");
        stack.Push(lower);
        stack.Push(upper);

        Assert.True(stack.CloseTop());

        Assert.Same(lower, stack.Top);
        Assert.True(lower.Shown);
        Assert.False(upper.Shown);
    }

    [Fact]
    public void CloseTopOnEmptyStackDoesNothing()
    {
        var stack = new UiWindowStack();

        Assert.False(stack.CloseTop());
    }

    // Escape must not reach past a conversation or a battle to the pause menu.
    [Fact]
    public void CloseTopRefusesWindowsThatDoNotCloseOnCancel()
    {
        var stack = new UiWindowStack();
        var battle = new FakeWindow("battle") { ClosesOnCancel = false };
        stack.Push(battle);

        Assert.False(stack.CloseTop());

        Assert.True(stack.AnyOpen);
        Assert.True(battle.Shown);
        Assert.Equal(0, battle.CloseRequests);
    }

    // The veto hook: MainMenu returns to its buttons from a sub-panel instead of
    // closing outright, which is what gives NewGameMenu an Escape exit.
    [Fact]
    public void CloseTopHonoursAWindowsVeto()
    {
        var stack = new UiWindowStack();
        var menu = new FakeWindow("menu") { RefuseClose = true };
        stack.Push(menu);

        Assert.False(stack.CloseTop());

        Assert.True(stack.AnyOpen);
        Assert.Equal(1, menu.CloseRequests);

        menu.RefuseClose = false;
        Assert.True(stack.CloseTop());
        Assert.False(stack.AnyOpen);
    }

    [Fact]
    public void PushingAnOpenWindowRaisesItInsteadOfDuplicating()
    {
        var stack = new UiWindowStack();
        var first = new FakeWindow("first");
        var second = new FakeWindow("second");
        stack.Push(first);
        stack.Push(second);

        stack.Push(first);

        Assert.Same(first, stack.Top);
        Assert.Equal(2, stack.Open.Count);
    }

    [Fact]
    public void PushingTheWindowAlreadyOnTopIsANoOp()
    {
        var stack = new UiWindowStack();
        var menu = new FakeWindow("menu");
        stack.Push(menu);
        var shownCount = menu.ShownHistory.Count;

        stack.Push(menu);

        Assert.Equal(shownCount, menu.ShownHistory.Count);
        Assert.Equal(1, stack.Open.Count);
    }

    [Fact]
    public void ClosingAWindowFromTheMiddleLeavesTheRestInOrder()
    {
        var stack = new UiWindowStack();
        var bottom = new FakeWindow("bottom");
        var middle = new FakeWindow("middle");
        var top = new FakeWindow("top");
        stack.Push(bottom);
        stack.Push(middle);
        stack.Push(top);

        Assert.True(stack.Close(middle));

        Assert.Equal(new IUiWindow[] { bottom, top }, stack.Open);
        Assert.False(middle.Shown);
    }

    [Fact]
    public void ClosingAWindowThatIsNotOpenReportsFalse()
    {
        var stack = new UiWindowStack();
        var stranger = new FakeWindow("stranger");

        Assert.False(stack.Close(stranger));
    }

    [Fact]
    public void CloseAllDropsEveryWindowRegardlessOfPolicy()
    {
        var stack = new UiWindowStack();
        var battle = new FakeWindow("battle") { ClosesOnCancel = false };
        var stubborn = new FakeWindow("stubborn") { RefuseClose = true };
        stack.Push(battle);
        stack.Push(stubborn);

        stack.CloseAll();

        Assert.False(stack.AnyOpen);
        Assert.False(battle.Shown);
        Assert.False(stubborn.Shown);
        Assert.False(stack.ReleasesPointer);
        Assert.False(stack.BlocksGameplay);
    }

    // Teardown must not depend on who closed the window: Escape and a Close
    // button have to leave the shop's merchant and the conversation's callback
    // in the same (dropped) state.
    [Fact]
    public void ClosingNotifiesTheWindowExactlyOnce()
    {
        var stack = new UiWindowStack();
        var shop = new FakeWindow("shop");
        stack.Push(shop);

        stack.Close(shop);

        Assert.Equal(1, shop.ClosedNotifications);
    }

    [Fact]
    public void BeingCoveredDoesNotCountAsClosing()
    {
        var stack = new UiWindowStack();
        var lower = new FakeWindow("lower");
        var cover = new FakeWindow("cover") { Exclusive = true };
        stack.Push(lower);

        stack.Push(cover);

        Assert.False(lower.Shown);
        Assert.Equal(0, lower.ClosedNotifications);
    }

    [Fact]
    public void CloseAllNotifiesEveryWindow()
    {
        var stack = new UiWindowStack();
        var first = new FakeWindow("first");
        var second = new FakeWindow("second");
        stack.Push(first);
        stack.Push(second);

        stack.CloseAll();

        Assert.Equal(1, first.ClosedNotifications);
        Assert.Equal(1, second.ClosedNotifications);
    }

    [Fact]
    public void ChangedFiresOncePerMutation()
    {
        var stack = new UiWindowStack();
        var menu = new FakeWindow("menu");
        var changes = 0;
        stack.Changed += () => changes++;

        stack.Push(menu);
        Assert.Equal(1, changes);

        stack.Close(menu);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void NullWindowsAreIgnored()
    {
        var stack = new UiWindowStack();

        stack.Push(null);

        Assert.False(stack.AnyOpen);
        Assert.False(stack.Close(null));
    }
}
