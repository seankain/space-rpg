using Godot;
using System;
using System.Collections.Generic;

public partial class LevelManager : Node3D
{
    // Set in _Ready; Main.tscn instances exactly one of these. Lets systems
    // that live outside the scene (BattleManager autoload) reach the level
    // root and menus.
    public static LevelManager Instance { get; private set; }

    [Export]
    public PackedScene PlayerScene;
    [Export]
    public PackedScene[] LevelScenes;

    [Export]
    public MainMenu Menu;

    [Export]
    public InGameMenu InGameMenu;

    [Export]
    public LoadingScreen LoadingScreen;

    [Export]
    public Node3D LevelRoot;
    public override void _Ready()
    {
        Instance = this;
        Menu.OnNewGameStarted += (o,creation)=>
        {
            var state = SaveManager.Instance.StartNewGame(creation);
            StartLevel(state.CurrentLevelPath);
        };
        Menu.OnGameLoadRequested += (o,save)=>
        {
            // LoadGameMenu already restored the GameState into SaveManager.
            StartLevel(SaveManager.Instance.CurrentState.CurrentLevelPath);
        };
    }

    // Single entry point for starting a level, whether from a new game or a
    // loaded save: clears whatever level is running, then streams in the next.
    public void StartLevel(string scenePath)
    {
        foreach (var child in LevelRoot.GetChildren())
        {
            child.QueueFree();
        }
        LoadingScreen.LoadNext(scenePath);
    }

    // Opens the two menus this scene owns. Closing them is not handled here:
    // UiWindowManager consumes ui_cancel whenever anything is open, so an event
    // reaching _UnhandledInput means the screen is clear and the key can only
    // mean "open". Both are bound to actions rather than raw keycodes so they
    // stay remappable.
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
            // Nothing to pause before a game exists; the pause menu is already
            // up at boot, and dismissing it would leave a blank screen.
            if (SaveManager.Instance?.GameInProgress == true)
            {
                GetViewport().SetInputAsHandled();
                UiWindowManager.Open(Menu);
            }
            return;
        }
        if (@event.IsActionPressed("Inventory"))
        {
            GetViewport().SetInputAsHandled();
            ToggleInGameMenu();
        }
    }

    // Inventory is the one action that both opens and closes its menu — unlike
    // Escape, nothing else consumes it first. It stays inert while another
    // window is up rather than opening a second menu behind it.
    private void ToggleInGameMenu()
    {
        if (UiWindowManager.IsOpen(InGameMenu))
        {
            UiWindowManager.Close(InGameMenu);
        }
        else if (!UiWindowManager.AnyOpen && SaveManager.Instance?.GameInProgress == true)
        {
            UiWindowManager.Open(InGameMenu);
        }
    }

    public void ChangeLevel(int levelIndex)
    {
        StartLevel(LevelScenes[levelIndex].ResourcePath);
    }

    // Game-over exit from a lost battle: the run is over, so drop the level
    // and put the player in front of the Load Game menu to restore a save
    // (New Game remains reachable behind its Back button).
    public void ShowGameOverLoadMenu()
    {
        foreach (var child in LevelRoot.GetChildren())
        {
            child.QueueFree();
        }
        // The run is over, so nothing that was up belongs on screen any more —
        // including windows that would normally refuse to close.
        UiWindowManager.CloseAll();
        UiWindowManager.Open(Menu);
        Menu.OnLoadGameButtonPressed();
    }
}
