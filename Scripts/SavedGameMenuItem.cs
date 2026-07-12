using Godot;
using System;

public partial class SavedGameMenuItem : Control
{
	public event Action<SaveData> SelectPressed;
	public event Action<SaveData> DeletePressed;

	[Export]
	public Label SaveNumber;
	[Export]
	public Label SaveDate;
	[Export]
	public Label LocationName;
	[Export]
	public Button SelectButton;
	[Export]
	public Button DeleteButton;

	public SaveData Save { get; private set; }

	public override void _Ready()
	{
		SelectButton.Pressed += () => { if (Save != null) { SelectPressed?.Invoke(Save); } };
		DeleteButton.Pressed += () => { if (Save != null) { DeletePressed?.Invoke(Save); } };
	}

	public void PopulateFromSavedData(SaveData saveData)
	{
		Save = saveData;
		this.SaveNumber.Text = $"#{saveData.SaveNumber}";
		this.SaveDate.Text = saveData.SaveTime.ToString("g");
		this.LocationName.Text = saveData.SaveLocationFriendlyName;
	}
}
