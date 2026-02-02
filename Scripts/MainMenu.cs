using Godot;
using Microsoft.VisualBasic;
using System;
using System.ComponentModel;

public partial class MainMenu : Control
{
    public event EventHandler OnNewGameStarted;
    [Export]
    public PackedScene PlayerScene;
    
     [Export]
     public Button NewGameButton;
     [Export]
     public Button LoadGameButton;
     [Export]
     public Button OptionsButton;
    [Export]
    public TextureButton QuitButton;
     [Export]
    public NewGameMenu NewGameMenu;

    public Control LoadGameMenu;

[Export]
    public VBoxContainer MenuButtonsContainer;

    public override void _Ready()
    {
        base._Ready();
        NewGameButton.Pressed += OnNewGameButtonPressed;
        LoadGameButton.Pressed += OnLoadGameButtonPressed;
        QuitButton.Pressed += OnQuitButtonPressed;
        NewGameMenu.StartButton.Pressed += ()=>{OnNewGameStarted?.Invoke(this,new());};
        // JoinButton.Pressed += OnJoinButtonPressed;

    }

    public void OnNewGameButtonPressed()
    {
        MenuButtonsContainer.Hide();
        NewGameMenu.Visible = true;
    }

    public void OnLoadGameButtonPressed()
    {
        MenuButtonsContainer.Hide();
        LoadGameMenu.Visible = true;
    }

    public void OnOptionsButtonPressed()
    {
        MenuButtonsContainer.Hide();

    }

    public void OnQuitButtonPressed()
    {
        GetTree().Quit();
    }

}
