using Godot;
using System;

public partial class Level : Node3D
{
    [Export]
    public PackedScene PlayerScene;
    public override void _Ready()
    {
        AddPlayer();
    }

    private void AddPlayer()
    {
        var player = (Node3D)PlayerScene.Instantiate();
        AddChild(player);
        var state = SaveManager.Instance?.CurrentState;
        if (state?.PlayerPosition != null)
        {
            player.GlobalPosition = SaveManager.ToGodot(state.PlayerPosition.Value);
            if (state.PlayerRotation != null)
            {
                player.Rotation = SaveManager.ToGodot(state.PlayerRotation.Value);
            }
            return;
        }
        foreach (var c in GetChildren())
        {
            if (c is Spawn spawn)
            {
                player.GlobalPosition = spawn.GlobalPosition;
                break;
            }
        }
    }

}
