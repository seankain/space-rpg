using Godot;
using System;

public partial class SaveGameMenu : Control
{
	public event EventHandler OnSaveGameMenuExitRequest;

	[Export]
	public PackedScene SavedGameMenuItemScene;

	[Export]
	public Button CreateSaveButton;

	[Export]
	public Button CancelButton;

	[Export]
	public VBoxContainer SaveVBox;

	private ConfirmationDialog confirmDialog;
	private Action confirmedAction;

	public override void _Ready()
	{
		CancelButton.ButtonDown += ()=>{OnSaveGameMenuExitRequest?.Invoke(this,new());};
		CreateSaveButton.ButtonDown += HandleCreateSave;
		VisibilityChanged += () => { if (Visible) { RefreshList(); } };
		confirmDialog = new ConfirmationDialog();
		AddChild(confirmDialog);
		confirmDialog.Confirmed += () => confirmedAction?.Invoke();
	}

	private void HandleCreateSave()
	{
		SaveManager.Instance.CreateSave();
		RefreshList();
	}

	private void RefreshList()
	{
		foreach (var child in SaveVBox.GetChildren())
		{
			SaveVBox.RemoveChild(child);
			child.QueueFree();
		}
		foreach (var save in SaveManager.Instance.ListSaves())
		{
			var row = SavedGameMenuItemScene.Instantiate<SavedGameMenuItem>();
			row.PopulateFromSavedData(save);
			row.SelectPressed += HandleOverwriteRequested;
			row.DeletePressed += HandleDeleteRequested;
			SaveVBox.AddChild(row);
		}
	}

	private void HandleOverwriteRequested(SaveData save)
	{
		Confirm($"Overwrite save #{save.SaveNumber}?", () =>
		{
			SaveManager.Instance.OverwriteSave(save);
			RefreshList();
		});
	}

	private void HandleDeleteRequested(SaveData save)
	{
		Confirm($"Delete save #{save.SaveNumber}? This cannot be undone.", () =>
		{
			SaveManager.Instance.DeleteSave(save);
			RefreshList();
		});
	}

	private void Confirm(string message, Action onConfirmed)
	{
		confirmedAction = onConfirmed;
		confirmDialog.DialogText = message;
		confirmDialog.PopupCentered();
	}

}
