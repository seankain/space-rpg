using Godot;
using System;
using System.Collections.Generic;

public partial class NewGameMenu : Control
{
    // Every stat starts at the base value; the pool is spent on top of that.
    public const uint BaseStatValue = 5;
    public const uint MaxStatValue = 10;
    public const int StatPointPool = 10;

    public event EventHandler<CharacterCreationData> OnCharacterCreated;

    [Export]
    public Button StartButton;
    [Export]
    public LineEdit NameEdit;
    [Export]
    public Label PointsRemainingLabel;
    [Export]
    public GridContainer StatsGrid;

    private static readonly string[] StatNames =
    {
        "Strength", "Intelligence", "Constitution", "Dexterity", "Wisdom", "Charisma"
    };

    private readonly Dictionary<string, uint> statValues = new();
    private readonly Dictionary<string, Label> statValueLabels = new();
    private readonly Dictionary<string, Button> increaseButtons = new();
    private readonly Dictionary<string, Button> decreaseButtons = new();
    private int pointsRemaining;

    public override void _Ready()
    {
        foreach (var statName in StatNames)
        {
            AddStatRow(statName);
        }
        NameEdit.TextChanged += _ => RefreshControls();
        StartButton.Pressed += OnStartButtonPressed;
        VisibilityChanged += () => { if (Visible) ResetForm(); };
        ResetForm();
    }

    public void ResetForm()
    {
        pointsRemaining = StatPointPool;
        NameEdit.Text = string.Empty;
        foreach (var statName in StatNames)
        {
            statValues[statName] = BaseStatValue;
        }
        RefreshControls();
    }

    private void AddStatRow(string statName)
    {
        var nameLabel = new Label
        {
            Text = statName,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        var decreaseButton = new Button { Text = "-", CustomMinimumSize = new Vector2(32, 0) };
        var valueLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(32, 0),
        };
        var increaseButton = new Button { Text = "+", CustomMinimumSize = new Vector2(32, 0) };

        decreaseButton.Pressed += () => ChangeStat(statName, -1);
        increaseButton.Pressed += () => ChangeStat(statName, +1);

        statValueLabels[statName] = valueLabel;
        decreaseButtons[statName] = decreaseButton;
        increaseButtons[statName] = increaseButton;

        StatsGrid.AddChild(nameLabel);
        StatsGrid.AddChild(decreaseButton);
        StatsGrid.AddChild(valueLabel);
        StatsGrid.AddChild(increaseButton);
    }

    private void ChangeStat(string statName, int delta)
    {
        var current = statValues[statName];
        if (delta > 0 && (pointsRemaining <= 0 || current >= MaxStatValue))
        {
            return;
        }
        if (delta < 0 && current <= BaseStatValue)
        {
            return;
        }
        statValues[statName] = (uint)(current + delta);
        pointsRemaining -= delta;
        RefreshControls();
    }

    private void RefreshControls()
    {
        PointsRemainingLabel.Text = $"Stat points remaining: {pointsRemaining}";
        foreach (var statName in StatNames)
        {
            statValueLabels[statName].Text = statValues[statName].ToString();
            increaseButtons[statName].Disabled = pointsRemaining <= 0 || statValues[statName] >= MaxStatValue;
            decreaseButtons[statName].Disabled = statValues[statName] <= BaseStatValue;
        }
        StartButton.Disabled = string.IsNullOrWhiteSpace(NameEdit.Text);
    }

    private void OnStartButtonPressed()
    {
        var name = NameEdit.Text.Trim();
        if (name.Length == 0)
        {
            return;
        }
        OnCharacterCreated?.Invoke(this, new CharacterCreationData
        {
            Name = name,
            Stats = new CharacterStats
            {
                Strength = statValues["Strength"],
                Intelligence = statValues["Intelligence"],
                Constitution = statValues["Constitution"],
                Dexterity = statValues["Dexterity"],
                Wisdom = statValues["Wisdom"],
                Charisma = statValues["Charisma"],
            },
        });
    }
}
