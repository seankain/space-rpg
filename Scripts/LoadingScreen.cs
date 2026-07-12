using Godot;
using System;

public partial class LoadingScreen : Control
{
	public event EventHandler LoadCompleted;

	[Export]
	private ProgressBar progressBar;
	public string NextScenePath {get; private set;}

	private Godot.Collections.Array progress = new();

	private bool isLoading = false;

	public void LoadNext(string nextScenePath)
	{
		NextScenePath = nextScenePath;
		ResourceLoader.LoadThreadedRequest(NextScenePath);
		progressBar.Value = 0;
		Show();
		isLoading = true;
	}

	public override void _Process(double delta)
	{
		if(!isLoading){return;}
		var status = ResourceLoader.LoadThreadedGetStatus(NextScenePath, progress);
		switch (status)
		{
			case ResourceLoader.ThreadLoadStatus.InProgress:
				if (progress.Count > 0)
				{
					progressBar.Value = progress[0].AsSingle() * 100.0;
				}
				break;
			case ResourceLoader.ThreadLoadStatus.Loaded:
				isLoading = false;
				var scene = (PackedScene)ResourceLoader.LoadThreadedGet(NextScenePath);
				GetNode("../LevelRoot").AddChild(scene.Instantiate());
				Hide();
				LoadCompleted?.Invoke(this, EventArgs.Empty);
				break;
			default:
				isLoading = false;
				Hide();
				GD.PushError($"Failed to load scene '{NextScenePath}'.");
				break;
		}
	}
}
