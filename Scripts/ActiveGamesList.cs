using Godot;
using System;

public partial class ActiveGamesList : Control
{
    [Export]
    public PackedScene ActiveGameRow;
    [Export]
    public VBoxContainer RowContainer;

    public override void _Ready()
    {
        base._Ready();
        AddDebugRows((uint)(Random.Shared.NextSingle() * 15));
    }

    public void AddDebugRows(uint rowCount)
    {
        for (var i = 0; i < rowCount; i++)
        {
            AddActiveGameRow($"Unnamed Server {i}", (uint)(Random.Shared.NextSingle() * 32), (uint)(Random.Shared.NextSingle() * 999));
        }
    }

    public void AddActiveGameRow(string name, uint activePlayers, uint latency)
    {
        GD.Load(ActiveGameRow.ResourcePath);
        var agr = ActiveGameRow.Instantiate<ActiveGameRow>();
        agr.ServerName.Text = name;
        agr.ActivePlayerCount.Text = activePlayers.ToString();
        agr.Latency.Text = latency.ToString();
        RowContainer.AddChild(agr);
    }

}
