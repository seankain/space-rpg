using Godot;
using System;
using System.Runtime.Serialization;

public partial class LoadingScreen : Control
{
	[Export]
	private ProgressBar progressBar;
	public string NextScenePath {get;set;} = "res://Scenes/Intro.tscn";

	private Godot.Collections.Array<float> progress = [];

	private bool isLoading = false;

	public void LoadNext(string nextScenePath)
	{
		NextScenePath = nextScenePath;
		ResourceLoader.Singleton.LoadThreadedRequest(NextScenePath);
		isLoading = true;
	}
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		if(!isLoading){return;}
		var status = ResourceLoader.Singleton.LoadThreadedGetStatus(NextScenePath, (Godot.Collections.Array)progress);
		var percentage = 0.0;
		switch (status)
		{
			case ResourceLoader.ThreadLoadStatus.InProgress:
			    percentage = progress[0] * 100.0;
				progressBar.Value = percentage;
			    break;
			case ResourceLoader.ThreadLoadStatus.Loaded:
			    var scene = (PackedScene)ResourceLoader.LoadThreadedGet(NextScenePath);
                GetNode("../LevelRoot").AddChild(scene.Instantiate());
			    break;
		}
	}
}
