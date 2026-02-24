using Godot;
using System;

public partial class SavedGameMenuItem : Control
{
	[Export]
	public Label SaveNumber;
	[Export]
	public Label SaveDate;
	[Export]
	public Label LocationName;
	public void PopulateFromSavedData(SaveData saveData)
	{
		this.SaveDate.Text = saveData.SaveCreationTime.ToString();
		this.SaveNumber.Text = saveData.SaveNumber.ToString();
		this.LocationName.Text = saveData.SaveLocationFriendlyName;
	}
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
