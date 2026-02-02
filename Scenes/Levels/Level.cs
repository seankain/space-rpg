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
        var p = GD.Load<PackedScene>(PlayerScene.ResourcePath);
        var player = p.Instantiate();
        AddChild(player);
        GD.Print("Adding player");
        var levelChildren = GetChildren();
        foreach (var c in levelChildren)
        {
            if (c is Spawn)
            {
                if (((Spawn)c).IsOccupied)
                    GD.Print("Setting player position");
                ((Node3D)player).GlobalPosition = ((Node3D)c).GlobalPosition;
            }
        }
    }

}
