using Godot;
using System;

public partial class ActiveGameRow : Control
{
    [Export]
    public Label ServerName;

    [Export]
    public Label ActivePlayerCount;

    [Export]
    public Label Latency;
    [Export]
    public Button JoinButton;
}
