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
        LoadingScreen.LoadCompleted += (o,e)=>{Input.MouseMode = Input.MouseModeEnum.Captured;};
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

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey eventKey)
        {
            if (eventKey.Pressed)
            {
                if(eventKey.Keycode == Key.Escape)
                {
                ToggleMenu();
                }
                if(eventKey.Keycode == Key.Tab)
                {
                    ToggleInGameMenu();
                }
            }


        }
    }

    private void ToggleMenu()
    {
        Menu.Visible = !Menu.Visible;
        if (Menu.Visible)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        else
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

        private void ToggleInGameMenu()
    {
        this.InGameMenu.Visible = !this.InGameMenu.Visible;
        if (this.InGameMenu.Visible)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        else
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
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
        Menu.Visible = true;
        Menu.OnLoadGameButtonPressed();
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }
}
