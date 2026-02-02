using Godot;
using System;
using System.Collections.Generic;

public partial class LevelManager : Node3D
{
    [Export]
    public PackedScene PlayerScene;
    [Export]
    public PackedScene[] LevelScenes;

    [Export]
    public MainMenu Menu;

    [Export]
    public LoadingScreen LoadingScreen;

    [Export]
    public Node3D LevelRoot;
    public override void _Ready()
    {
        Menu.OnNewGameStarted += (o,e)=>{LoadingScreen.LoadNext("res://Scenes/Levels/Intro.tscn");};
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey eventKey)
        {
            if (eventKey.Pressed && eventKey.Keycode == Key.Escape)
            {
                ToggleMenu();
            }
        }
    }

    private void ToggleMenu()
    {
        Menu.Visible = !Menu.Visible;
        if (Menu.Visible)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        else
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    private void AddPlayer(long peerId)
    {
        var p = GD.Load<PackedScene>(PlayerScene.ResourcePath);
        var player = p.Instantiate();
        player.Name = peerId.ToString();
        AddChild(player);
        GD.Print("Adding player");
        foreach (var spawnNode in GetTree().GetNodesInGroup("SpawnPositions"))
        {
            GD.Print("Spawn node");
            if (spawnNode.Name == $"spawn{player.Name}")
            {
                GD.Print("Setting player position");
                ((Node3D)player).GlobalPosition = ((Node3D)spawnNode).GlobalPosition;
            }
        }

    }

    public void ChangeLevel(int levelIndex)
    {
        var levelScene = GD.Load<PackedScene>(LevelScenes[levelIndex].ResourcePath);
        var levelInstance = levelScene.Instantiate();
        GetNode("LevelRoot").AddChild(levelInstance);

    }
}
