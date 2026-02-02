using Godot;
using System;
using System.Collections.Generic;

public partial class Spawn : Node3D
{
    private Area3D triggerArea;

    private List<Node3D> occupiers = new List<Node3D>();

    public override void _Ready()
    {
        foreach (var c in this.GetChildren())
        {
            if (c is Area3D)
            {
                triggerArea = c as Area3D;
                triggerArea.BodyEntered += HandleAreaEntered;
                triggerArea.BodyExited += HandleAreaExited;
            }
        }
    }

    private void HandleAreaExited(Node3D body)
    {
        if (body is Player)
        {
            this.occupiers.Add(body);
        }
    }


    private void HandleAreaEntered(Node3D body)
    {
        if (this.occupiers.Contains(body))
        {
            this.occupiers.Remove(body);
        }

    }


    public bool IsOccupied { get { return this.occupiers.Count > 0; } }

}
