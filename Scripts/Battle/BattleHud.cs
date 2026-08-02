using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// Code-built UI for a running battle, same single-file style as
// DialogueManager: a top message bar, a party status panel (HP/PP), and a
// bottom-right action menu that walks Attack / Power / Item down to a target
// pick. PromptAction hands the finished choice back to BattleScene as a Task
// so the turn loop can simply await the player.
public partial class BattleHud : CanvasLayer
{
    private Label messageLabel;
    private VBoxContainer partyRows;
    private PanelContainer actionPanel;
    private Label actionTitle;
    private VBoxContainer actionButtons;
    private PanelContainer gameOverPanel;
    private Button loadSaveButton;

    private TaskCompletionSource<BattleAction> pendingChoice;

    public override void _Ready()
    {
        Layer = UiLayers.BattleHud;
        BuildUi();
    }

    public void ShowMessage(string text)
    {
        messageLabel.Text = text;
    }

    // Rebuilds the party readout; called after anything changes HP or PP.
    public void UpdateParty(IReadOnlyList<BattleCombatant> party)
    {
        foreach (var child in partyRows.GetChildren())
        {
            child.QueueFree();
        }
        foreach (var member in party)
        {
            var row = new Label
            {
                Text = $"{member.Name}   HP {member.HealthPoints}/{member.MaxHealthPoints}   PP {member.PowerPoints}/{member.MaxPowerPoints}",
            };
            row.AddThemeFontSizeOverride("font_size", 18);
            if (member.IsDown)
            {
                row.Modulate = new Color(1f, 0.4f, 0.4f, 0.8f);
                row.Text += "   DOWN";
            }
            partyRows.AddChild(row);
        }
    }

    // Shows the action menu for one party member's turn and resolves once a
    // complete action (kind + target) has been picked.
    public Task<BattleAction> PromptAction(
        BattleCombatant actor,
        List<BattleCombatant> enemies,
        List<BattleCombatant> party,
        Inventory inventory)
    {
        pendingChoice = new TaskCompletionSource<BattleAction>();
        actionPanel.Visible = true;
        actionTitle.Text = $"{actor.Name}'s turn";
        ShowRootMenu(actor, enemies, party, inventory);
        return pendingChoice.Task;
    }

    private void ShowRootMenu(
        BattleCombatant actor,
        List<BattleCombatant> enemies,
        List<BattleCombatant> party,
        Inventory inventory)
    {
        ClearActionButtons();

        AddActionButton("Attack", () =>
            ShowTargetMenu(LivingTargets(enemies), target => Complete(new BattleAction
            {
                Kind = BattleActionKind.Attack,
                Actor = actor,
                Target = target,
            }), () => ShowRootMenu(actor, enemies, party, inventory)));

        var powerButton = AddActionButton("Power", () =>
            ShowPowerMenu(actor, enemies, party, inventory));
        powerButton.Disabled = actor.Powers.Count == 0;

        var usableItems = ConsumableStacks(inventory);
        var itemButton = AddActionButton("Item", () =>
            ShowItemMenu(actor, enemies, party, inventory, usableItems));
        itemButton.Disabled = usableItems.Count == 0;

        FocusFirstButton();
    }

    private void ShowPowerMenu(
        BattleCombatant actor,
        List<BattleCombatant> enemies,
        List<BattleCombatant> party,
        Inventory inventory)
    {
        ClearActionButtons();
        foreach (var power in actor.Powers)
        {
            var captured = power;
            var button = AddActionButton($"{power.Name} ({power.PowerPointCost} PP)", () =>
            {
                var targets = captured.Effect == PowerEffect.Heal
                    ? LivingTargets(party)
                    : LivingTargets(enemies);
                ShowTargetMenu(targets, target => Complete(new BattleAction
                {
                    Kind = BattleActionKind.Power,
                    Actor = actor,
                    Target = target,
                    Power = captured,
                }), () => ShowPowerMenu(actor, enemies, party, inventory));
            });
            button.Disabled = actor.PowerPoints < power.PowerPointCost;
            button.TooltipText = power.Description;
        }
        AddBackButton(() => ShowRootMenu(actor, enemies, party, inventory));
        FocusFirstButton();
    }

    private void ShowItemMenu(
        BattleCombatant actor,
        List<BattleCombatant> enemies,
        List<BattleCombatant> party,
        Inventory inventory,
        List<(ConsumableItem Item, uint Count)> usableItems)
    {
        ClearActionButtons();
        foreach (var (item, count) in usableItems)
        {
            var captured = item;
            var button = AddActionButton($"{item.Name} ×{count}", () =>
                ShowTargetMenu(LivingTargets(party), target => Complete(new BattleAction
                {
                    Kind = BattleActionKind.Item,
                    Actor = actor,
                    Target = target,
                    ItemId = captured.Id,
                }), () => ShowItemMenu(actor, enemies, party, inventory, usableItems)));
            button.TooltipText = item.Description;
        }
        AddBackButton(() => ShowRootMenu(actor, enemies, party, inventory));
        FocusFirstButton();
    }

    private void ShowTargetMenu(
        List<BattleCombatant> targets,
        Action<BattleCombatant> onPicked,
        Action onBack)
    {
        ClearActionButtons();
        foreach (var target in targets)
        {
            var captured = target;
            AddActionButton($"{target.Name} (HP {target.HealthPoints}/{target.MaxHealthPoints})",
                () => onPicked(captured));
        }
        AddBackButton(onBack);
        FocusFirstButton();
    }

    private static List<BattleCombatant> LivingTargets(List<BattleCombatant> side) =>
        side.Where(c => !c.IsDown).ToList();

    // Inventory stacks collapsed to one entry per consumable definition.
    private static List<(ConsumableItem Item, uint Count)> ConsumableStacks(Inventory inventory) =>
        inventory.Stacks
            .GroupBy(s => s.ItemId)
            .Select(g => (Item: ItemCatalog.Get(g.Key) as ConsumableItem, Count: (uint)g.Sum(s => s.Quantity)))
            .Where(e => e.Item != null && e.Count > 0)
            .ToList();

    private void Complete(BattleAction action)
    {
        actionPanel.Visible = false;
        ClearActionButtons();
        var pending = pendingChoice;
        pendingChoice = null;
        pending?.TrySetResult(action);
    }

    // Terminal defeat screen; the only way forward is loading a save (or the
    // main menu behind it), which the callback owns.
    public void ShowGameOver(Action onLoadSave)
    {
        actionPanel.Visible = false;
        gameOverPanel.Visible = true;
        loadSaveButton.Pressed += () => onLoadSave?.Invoke();
        loadSaveButton.CallDeferred(Control.MethodName.GrabFocus);
    }

    private Button AddActionButton(string label, Action onPressed)
    {
        var button = new Button { Text = label, Alignment = HorizontalAlignment.Left };
        button.Pressed += () => onPressed();
        actionButtons.AddChild(button);
        return button;
    }

    private void AddBackButton(Action onBack)
    {
        var button = new Button { Text = "Back", Alignment = HorizontalAlignment.Left };
        button.Pressed += () => onBack();
        actionButtons.AddChild(button);
    }

    private void ClearActionButtons()
    {
        foreach (var child in actionButtons.GetChildren())
        {
            actionButtons.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void FocusFirstButton()
    {
        if (actionButtons.GetChildCount() > 0 && actionButtons.GetChild(0) is Button first)
        {
            first.CallDeferred(Control.MethodName.GrabFocus);
        }
    }

    private void BuildUi()
    {
        // Top-center message bar.
        var messagePanel = new PanelContainer
        {
            AnchorLeft = 0.2f,
            AnchorRight = 0.8f,
            AnchorTop = 0f,
            AnchorBottom = 0f,
            OffsetTop = 20,
            GrowVertical = Control.GrowDirection.End,
        };
        AddChild(messagePanel);
        var messageMargin = MakeMargin(16, 10);
        messagePanel.AddChild(messageMargin);
        messageLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        messageLabel.AddThemeFontSizeOverride("font_size", 20);
        messageMargin.AddChild(messageLabel);

        // Bottom-left party status.
        var partyPanel = new PanelContainer
        {
            AnchorLeft = 0f,
            AnchorRight = 0f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetLeft = 20,
            OffsetBottom = -20,
            GrowHorizontal = Control.GrowDirection.End,
            GrowVertical = Control.GrowDirection.Begin,
        };
        AddChild(partyPanel);
        var partyMargin = MakeMargin(14, 10);
        partyPanel.AddChild(partyMargin);
        partyRows = new VBoxContainer();
        partyRows.AddThemeConstantOverride("separation", 4);
        partyMargin.AddChild(partyRows);

        // Bottom-right action menu.
        actionPanel = new PanelContainer
        {
            AnchorLeft = 1f,
            AnchorRight = 1f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetLeft = -320,
            OffsetRight = -20,
            OffsetBottom = -20,
            GrowVertical = Control.GrowDirection.Begin,
            Visible = false,
        };
        AddChild(actionPanel);
        var actionMargin = MakeMargin(14, 10);
        actionPanel.AddChild(actionMargin);
        var actionColumn = new VBoxContainer();
        actionColumn.AddThemeConstantOverride("separation", 6);
        actionMargin.AddChild(actionColumn);
        actionTitle = new Label();
        actionTitle.AddThemeFontSizeOverride("font_size", 20);
        actionTitle.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f));
        actionColumn.AddChild(actionTitle);
        actionButtons = new VBoxContainer();
        actionButtons.AddThemeConstantOverride("separation", 4);
        actionColumn.AddChild(actionButtons);

        // Centered game-over panel, hidden until the party falls.
        gameOverPanel = new PanelContainer
        {
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 0.5f,
            AnchorBottom = 0.5f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
            Visible = false,
        };
        AddChild(gameOverPanel);
        var gameOverMargin = MakeMargin(30, 24);
        gameOverPanel.AddChild(gameOverMargin);
        var gameOverColumn = new VBoxContainer();
        gameOverColumn.AddThemeConstantOverride("separation", 12);
        gameOverMargin.AddChild(gameOverColumn);
        var gameOverTitle = new Label
        {
            Text = "Game Over",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        gameOverTitle.AddThemeFontSizeOverride("font_size", 36);
        gameOverTitle.AddThemeColorOverride("font_color", new Color(0.9f, 0.25f, 0.25f));
        gameOverColumn.AddChild(gameOverTitle);
        var gameOverText = new Label
        {
            Text = "Your party has fallen.\nLoad a previous save to continue.",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        gameOverText.AddThemeFontSizeOverride("font_size", 18);
        gameOverColumn.AddChild(gameOverText);
        loadSaveButton = new Button { Text = "Load a Save" };
        gameOverColumn.AddChild(loadSaveButton);
    }

    private static MarginContainer MakeMargin(int horizontal, int vertical)
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", horizontal);
        margin.AddThemeConstantOverride("margin_right", horizontal);
        margin.AddThemeConstantOverride("margin_top", vertical);
        margin.AddThemeConstantOverride("margin_bottom", vertical);
        return margin;
    }
}
