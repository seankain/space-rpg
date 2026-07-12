using Godot;
using System;

public partial class MainMenu : Control
{
    public event EventHandler<CharacterCreationData> OnNewGameStarted;
    public event EventHandler<SaveData> OnGameLoadRequested;
    [Export]
    public PackedScene PlayerScene;

     [Export]
     public Button NewGameButton;
     [Export]
     public Button LoadGameButton;
     [Export]
     public Button OptionsButton;
    [Export]
    public Button QuitButton;
    [Export]
    public Button SaveGameButton;
     [Export]
    public NewGameMenu NewGameMenu;
    [Export]
    public LoadGameMenu LoadGameMenu;
    [Export]
    public SaveGameMenu SaveGameMenu;

    [Export]
    public VBoxContainer MenuButtonsContainer;


    public override void _Ready()
    {
        base._Ready();
        NewGameButton.Pressed += OnNewGameButtonPressed;
        LoadGameButton.Pressed += OnLoadGameButtonPressed;
        QuitButton.Pressed += OnQuitButtonPressed;
        SaveGameButton.Pressed += OnSaveButtonPressed;
        NewGameMenu.OnCharacterCreated += (o,creation)=>
        {
            this.SaveGameButton.Disabled = false;
            this.Reset();
            this.Hide();
            OnNewGameStarted?.Invoke(this, creation);
        };
        SaveGameMenu.OnSaveGameMenuExitRequest += (o,e)=>{Reset();};
        LoadGameMenu.OnExitRequest += (o,e)=>{Reset();};
        LoadGameMenu.OnLoadRequested += (o,save)=>
        {
            this.SaveGameButton.Disabled = false;
            this.Reset();
            this.Hide();
            OnGameLoadRequested?.Invoke(this, save);
        };
    }

    private void Reset()
    {
        this.NewGameMenu.Visible = false;
        this.LoadGameMenu.Visible = false;
        this.SaveGameMenu.Visible = false;
        this.MenuButtonsContainer.Show();
    }

    private void OnSaveButtonPressed()
    {
        MenuButtonsContainer.Hide();
        this.NewGameMenu.Visible = false;
        this.LoadGameMenu.Visible = false;
        this.SaveGameMenu.Visible = true;
    }


    public void OnNewGameButtonPressed()
    {
        MenuButtonsContainer.Hide();
        this.LoadGameMenu.Visible = false;
        this.SaveGameMenu.Visible = false;
        NewGameMenu.Visible = true;
    }

    public void OnLoadGameButtonPressed()
    {
        MenuButtonsContainer.Hide();
        this.SaveGameMenu.Visible = false;
        this.NewGameMenu.Visible = false;
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
