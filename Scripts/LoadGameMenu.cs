using Godot;
using System;

public partial class LoadGameMenu : Control
{
	public event EventHandler OnExitRequest;
	// Raised after the save's GameState has been restored into SaveManager;
	// LevelManager reacts by loading the restored level.
	public event EventHandler<SaveData> OnLoadRequested;

	[Export]
	public PackedScene SavedGameMenuItemScene;

	[Export]
	public Button CancelButton;

	[Export]
	public VBoxContainer SaveVBox;

	[Export]
	public Label EmptyListLabel;

	private ConfirmationDialog confirmDialog;
	private Action confirmedAction;
	private AcceptDialog errorDialog;

	public override void _Ready()
	{
		CancelButton.ButtonDown += ()=>{OnExitRequest?.Invoke(this,new());};
		VisibilityChanged += () => { if (Visible) { RefreshList(); } };
		confirmDialog = new ConfirmationDialog();
		AddChild(confirmDialog);
		confirmDialog.Confirmed += () => confirmedAction?.Invoke();
		errorDialog = new AcceptDialog();
		AddChild(errorDialog);
	}

	private void RefreshList()
	{
		foreach (var child in SaveVBox.GetChildren())
		{
			SaveVBox.RemoveChild(child);
			child.QueueFree();
		}
		var saves = SaveManager.Instance.ListSaves();
		EmptyListLabel.Visible = saves.Count == 0;
		foreach (var save in saves)
		{
			var row = SavedGameMenuItemScene.Instantiate<SavedGameMenuItem>();
			row.PopulateFromSavedData(save);
			row.SelectPressed += HandleLoadRequested;
			row.DeletePressed += HandleDeleteRequested;
			SaveVBox.AddChild(row);
		}
	}

	private void HandleLoadRequested(SaveData save)
	{
		try
		{
			SaveManager.Instance.LoadSave(save);
		}
		catch (Exception e)
		{
			GD.PushError($"Failed to load save #{save.SaveNumber}: {e.Message}");
			errorDialog.DialogText = $"Save #{save.SaveNumber} could not be loaded. The file may be corrupted.";
			errorDialog.PopupCentered();
			return;
		}
		OnLoadRequested?.Invoke(this, save);
	}

	private void HandleDeleteRequested(SaveData save)
	{
		confirmedAction = () =>
		{
			SaveManager.Instance.DeleteSave(save);
			RefreshList();
		};
		confirmDialog.DialogText = $"Delete save #{save.SaveNumber}? This cannot be undone.";
		confirmDialog.PopupCentered();
	}

}
