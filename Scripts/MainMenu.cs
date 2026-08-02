using Godot;
using System;

// The title screen, and the pause menu once a game is running. Its three
// sub-panels are one-of-N: exactly one is up, or none and the buttons show.
public partial class MainMenu : Control, IUiWindow
{
    public event EventHandler<CharacterCreationData> OnNewGameStarted;
    public event EventHandler<SaveData> OnGameLoadRequested;

    // The sub-panel currently up, or null when the buttons are showing. Kept so
    // Escape can back out one level instead of closing the whole menu.
    private Control activePanel;

    // Every sub-panel, so showing one hides the others without each caller
    // having to name them all. A fourth panel joins this list and nothing else.
    private Control[] panels;
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


    public string WindowName => "main menu";

    // It wants the whole screen, and at boot it is the screen.
    public bool Exclusive => true;

    public void SetShown(bool shown) => Visible = shown;

    // Escape backs out of a sub-panel to the buttons before it closes the menu
    // itself — which is also how NewGameMenu, which never had an exit of its
    // own, got one. Refusing the close when no game is running keeps the title
    // screen from being dismissed into a blank screen.
    public bool RequestClose()
    {
        if (activePanel != null)
        {
            ShowPanel(null);
            return false;
        }
        return SaveManager.Instance?.GameInProgress == true;
    }

    public override void _Ready()
    {
        base._Ready();
        panels = new Control[] { NewGameMenu, LoadGameMenu, SaveGameMenu };
        // The title screen is up before anything else, so it opens itself.
        UiWindowManager.Open(this);
        // No Options panel exists yet; leaving the button live would hide the
        // buttons and show nothing, with no way back.
        OptionsButton.Disabled = true;
        NewGameButton.Pressed += OnNewGameButtonPressed;
        LoadGameButton.Pressed += OnLoadGameButtonPressed;
        QuitButton.Pressed += OnQuitButtonPressed;
        SaveGameButton.Pressed += OnSaveButtonPressed;
        NewGameMenu.OnCharacterCreated += (o,creation)=>
        {
            this.SaveGameButton.Disabled = false;
            this.Reset();
            UiWindowManager.Close(this);
            OnNewGameStarted?.Invoke(this, creation);
        };
        SaveGameMenu.OnSaveGameMenuExitRequest += (o,e)=>{Reset();};
        LoadGameMenu.OnExitRequest += (o,e)=>{Reset();};
        LoadGameMenu.OnLoadRequested += (o,save)=>
        {
            this.SaveGameButton.Disabled = false;
            this.Reset();
            UiWindowManager.Close(this);
            OnGameLoadRequested?.Invoke(this, save);
        };
    }

    // Shows one sub-panel and hides the rest, or shows the buttons when passed
    // null. The single place that decides what is up inside this menu.
    private void ShowPanel(Control panel)
    {
        foreach (var candidate in panels)
        {
            candidate.Visible = candidate == panel;
        }
        MenuButtonsContainer.Visible = panel == null;
        activePanel = panel;
    }

    private void Reset() => ShowPanel(null);

    private void OnSaveButtonPressed() => ShowPanel(SaveGameMenu);

    public void OnNewGameButtonPressed() => ShowPanel(NewGameMenu);

    public void OnLoadGameButtonPressed() => ShowPanel(LoadGameMenu);

    public void OnQuitButtonPressed()
    {
        GetTree().Quit();
    }

}
