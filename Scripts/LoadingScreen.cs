using Godot;

public partial class LoadingScreen : Control, IUiWindow
{
	public string WindowName => "loading screen";

	// Covers whatever it is loading over, and can't be dismissed — it goes away
	// when the load finishes. Being a window is what recaptures the pointer for
	// gameplay on the way out, which LevelManager used to do by hand.
	public bool Exclusive => true;

	public bool ClosesOnCancel => false;

	public void SetShown(bool shown) => Visible = shown;

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
		UiWindowManager.Open(this);
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
				UiWindowManager.Close(this);
				break;
			default:
				isLoading = false;
				UiWindowManager.Close(this);
				GD.PushError($"Failed to load scene '{NextScenePath}'.");
				break;
		}
	}
}
