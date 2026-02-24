using Godot;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

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

	private List<SaveData> saves = new();

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		CancelButton.ButtonDown += ()=>{OnSaveGameMenuExitRequest?.Invoke(this,new());};
		CreateSaveButton.ButtonDown += HandleCreateSave;
	}

    private void HandleCreateSave()
    {
		var save = new SaveData
		{
			SaveCreationTime = DateTime.Now,
			SaveLocationFriendlyName = "Tutorial",
			SaveLocationId = Guid.NewGuid(),
			SaveTime = DateTime.Now,
			SaveNumber = (uint)saves.Count
		};
        saves.Add(save);
		AddSaveToDisplay(save);
    }


    public override void _Input(InputEvent @event)
    {
		if(!this.Visible){return;}
        if(@event is InputEventKey keyEvent)
		{
			if(keyEvent.Keycode == Key.Escape)
			{
				OnSaveGameMenuExitRequest?.Invoke(this,new());
			}
		}
    }

	public void AddSaveToDisplay(SaveData save)
	{
		var ps = GD.Load<PackedScene>(SavedGameMenuItemScene.ResourcePath);
        var SavedGameMenuItem = ps.Instantiate();
		if(SavedGameMenuItem is SavedGameMenuItem smi)
		{
			smi.PopulateFromSavedData(save);
		}
        SaveVBox.AddChild(SavedGameMenuItem);
	}

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta)
	{
	}
}
