using Godot;
using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

public partial class SaveGameMenu : Control
{
	public event EventHandler OnSaveGameMenuExitRequest;

	[Export]
	public PackedScene SavedGameMenuItemScene;
	
	

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		
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

	public void AddSaveToDisplay()
	{
		
	}

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta)
	{
	}
}
