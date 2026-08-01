using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

// The in-world NPC editor: the form that opens when the editor-mode toolbar
// places a new NPC or the Edit NPC tool clicks an existing one. It edits the
// engine-free NpcEditModel — identity, the character mesh/rig, the character
// sheet, starting items and credits — validates on every keystroke, and asks
// DevConsole to write the .tres when the author saves.
//
// Code-built CanvasLayer like DialogueEditorPanel, and split the same way:
// this class renders and mutates the working model, DevConsole owns the
// panel's lifecycle, the placement preview, and the file I/O.
public partial class NpcEditorPanel : CanvasLayer
{
    // Raised when the panel asks to close (Close button or Escape).
    public Action Closed { get; set; }

    // Raised by the Save button; DevConsole applies the model and writes.
    public Action SaveRequested { get; set; }

    // Raised on a change the standing preview can't absorb in place — a rig
    // swap; DevConsole re-spawns the pending body.
    public Action PreviewRefreshRequested { get; set; }

    // Raised as the facing spinner moves. Separate from the refresh above
    // because turning a body is a rotation, not a re-spawn, and a spinner drag
    // would otherwise rebuild the NPC dozens of times a second.
    public Action<float> FacingChanged { get; set; }

    // The working copy. Nothing is written to the definition — or to disk —
    // until DevConsole applies it on save. Seeded with defaults so the widgets
    // built in _Ready have something to read before the first Edit.
    public NpcEditModel Model { get; private set; } = new();

    // The definition being edited, and whether saving creates its file. A new
    // NPC's id is editable; an existing one's is not — ids are referenced by
    // quests, saves, and dialogue and are permanent once shipped.
    public NpcDefinition Definition { get; private set; }
    public bool IsNew { get; private set; }

    private static readonly Color HeadingColor = new(1f, 0.85f, 0.4f);
    private static readonly Color MutedColor = new(0.65f, 0.67f, 0.7f);
    private static readonly Color ErrorColor = new(1f, 0.5f, 0.45f);
    private static readonly Color OkColor = new(0.55f, 0.85f, 0.6f);

    private const string NoRigLabel = "(none — capsule)";

    private Label titleLabel;
    private Label placementLabel;
    private Label validationLabel;
    private Label statusLabel;
    private LineEdit idEdit;
    private LineEdit nameEdit;
    private OptionButton rigPicker;
    private SpinBox rotationSpin;
    private SpinBox creditsSpin;
    private VBoxContainer itemRows;
    private OptionButton addItemPicker;
    private Button saveButton;

    private readonly List<string> rigNames = new();
    private readonly List<uint> catalogItemIds = new();
    private readonly Dictionary<string, SpinBox> statSpins = new();
    private bool suppressSignals;

    public override void _Ready()
    {
        Layer = 90; // above the toolbar (80), below the console (100)
        Visible = false;
        BuildChrome();
    }

    // Open the form on a definition. `isNew` is true for a placement that has
    // no file yet, which is the only time the id can be typed.
    public void Edit(NpcDefinition definition, bool isNew, string placementSummary)
    {
        Definition = definition;
        IsNew = isNew;
        Model = NpcEditMapping.ToModel(definition);
        titleLabel.Text = isNew ? "New NPC" : $"Editing:  {definition.NpcId}";
        placementLabel.Text = placementSummary ?? "";
        SetStatus(isNew
            ? "Fill in the sheet, then Save to write the .tres."
            : "Save writes the .tres and re-spawns this NPC in the world.", true);
        RefreshForm();
    }

    public void SetStatus(string text, bool ok)
    {
        statusLabel.Text = text ?? "";
        statusLabel.AddThemeColorOverride("font_color", ok ? OkColor : ErrorColor);
    }

    public override void _Input(InputEvent @event)
    {
        if (Visible && @event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            Closed?.Invoke();
        }
    }

    // --- Form -----------------------------------------------------------------

    private void RefreshForm()
    {
        suppressSignals = true;
        idEdit.Text = Model.NpcId;
        idEdit.Editable = IsNew;
        idEdit.TooltipText = IsNew
            ? "Stable id; becomes the file name. Permanent once shipped."
            : "Ids are permanent — quests, saves, and dialogue reference them.";
        nameEdit.Text = Model.DisplayName;
        RefreshRigPicker();
        rotationSpin.Value = Model.RotationDegreesY;
        creditsSpin.Value = Model.Credits;
        statSpins["Level"].Value = Model.Level;
        statSpins["Max HP"].Value = Model.MaxHealthPoints;
        statSpins["Max PP"].Value = Model.MaxMagicPoints;
        statSpins["Strength"].Value = Model.Stats.Strength;
        statSpins["Intelligence"].Value = Model.Stats.Intelligence;
        statSpins["Constitution"].Value = Model.Stats.Constitution;
        statSpins["Dexterity"].Value = Model.Stats.Dexterity;
        statSpins["Wisdom"].Value = Model.Stats.Wisdom;
        statSpins["Charisma"].Value = Model.Stats.Charisma;
        suppressSignals = false;
        RebuildItemRows();
        RefreshValidation();
    }

    private void RefreshRigPicker()
    {
        rigPicker.Clear();
        rigNames.Clear();
        rigNames.Add(NpcEditModel.NoRig);
        rigPicker.AddItem(NoRigLabel);
        foreach (var name in EditorPlacement.RigNames())
        {
            rigNames.Add(name);
            rigPicker.AddItem(name);
        }
        var index = rigNames.FindIndex(n => n == Model.RigName);
        if (index < 0)
        {
            // A rig that no longer exists on disk: keep the author's value
            // visible rather than silently resetting them to the capsule.
            rigNames.Add(Model.RigName);
            rigPicker.AddItem($"{Model.RigName}  (missing)");
            index = rigNames.Count - 1;
        }
        rigPicker.Select(index);
    }

    private void OnRigSelected(long index)
    {
        if (suppressSignals || index < 0 || index >= rigNames.Count)
        {
            return;
        }
        Model.RigName = rigNames[(int)index];
        RefreshValidation();
        PreviewRefreshRequested?.Invoke();
    }

    private void RebuildItemRows()
    {
        // Detach before freeing: QueueFree alone leaves the old rows in the
        // container for a frame, so the rebuilt list would briefly show double.
        foreach (var child in itemRows.GetChildren())
        {
            itemRows.RemoveChild(child);
            child.QueueFree();
        }
        if (Model.Items.Count == 0)
        {
            AddText(itemRows, "  (nothing)", MutedColor, 14);
        }
        for (var i = 0; i < Model.Items.Count; i++)
        {
            AddItemRow(i);
        }
    }

    private void AddItemRow(int index)
    {
        var stack = Model.Items[index];
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        itemRows.AddChild(row);

        var item = ItemCatalog.Get(stack.ItemId);
        var label = new Label
        {
            Text = item == null ? $"#{stack.ItemId}  (unknown item)" : $"{item.Name}  (#{stack.ItemId})",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        label.AddThemeColorOverride("font_color", item == null ? ErrorColor : Colors.White);
        row.AddChild(label);

        var quantity = new SpinBox { MinValue = 1, MaxValue = 999, Value = stack.Quantity };
        quantity.ValueChanged += value =>
        {
            if (!suppressSignals)
            {
                stack.Quantity = (uint)value;
                RefreshValidation();
            }
        };
        row.AddChild(quantity);

        var remove = new Button { Text = "✕", FocusMode = Control.FocusModeEnum.None };
        remove.AddThemeColorOverride("font_color", ErrorColor);
        remove.Pressed += () =>
        {
            Model.RemoveItem(index);
            RebuildItemRows();
            RefreshValidation();
        };
        row.AddChild(remove);
    }

    private void OnAddItem()
    {
        var selected = addItemPicker.Selected;
        if (selected < 0 || selected >= catalogItemIds.Count)
        {
            return;
        }
        Model.AddItem(catalogItemIds[selected], 1);
        RebuildItemRows();
        RefreshValidation();
    }

    // --- Validation -----------------------------------------------------------

    // The problems blocking a save, so DevConsole can refuse before writing.
    public IReadOnlyList<string> Problems() => Model.Validate(IdTaken);

    // A new NPC may not reuse an existing id; an existing one already owns its
    // own, and can't be renamed, so nothing is taken from its point of view.
    private bool IdTaken(string id) => IsNew && NpcDatabase.Get(id) != null;

    private void RefreshValidation()
    {
        var problems = Problems();
        saveButton.Disabled = problems.Count > 0;
        if (problems.Count == 0)
        {
            validationLabel.Text = "No problems.";
            validationLabel.AddThemeColorOverride("font_color", OkColor);
            return;
        }
        validationLabel.Text = string.Join("\n", problems.Take(6).Select(p => "• " + p));
        validationLabel.AddThemeColorOverride("font_color", ErrorColor);
    }

    // --- Chrome ---------------------------------------------------------------

    private void BuildChrome()
    {
        var panel = new PanelContainer
        {
            // Left of the toolbar strip, so both stay clickable together.
            AnchorLeft = 0.06f,
            AnchorRight = 0.06f,
            AnchorTop = 0.08f,
            AnchorBottom = 0.94f,
            OffsetRight = 520,
        };
        panel.AddThemeStyleboxOverride(
            "panel", new StyleBoxFlat { BgColor = new Color(0.06f, 0.07f, 0.09f, 0.97f) });
        AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_right", 14);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        panel.AddChild(margin);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 8);
        margin.AddChild(root);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 10);
        root.AddChild(header);

        titleLabel = new Label { Text = "NPC", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        titleLabel.AddThemeFontSizeOverride("font_size", 22);
        titleLabel.AddThemeColorOverride("font_color", HeadingColor);
        header.AddChild(titleLabel);

        saveButton = new Button { Text = "Save" };
        saveButton.Pressed += () => SaveRequested?.Invoke();
        header.AddChild(saveButton);

        var close = new Button { Text = "Close  (Esc)" };
        close.Pressed += () => Closed?.Invoke();
        header.AddChild(close);

        placementLabel = new Label { Text = "" };
        placementLabel.AddThemeColorOverride("font_color", MutedColor);
        placementLabel.AddThemeFontSizeOverride("font_size", 13);
        root.AddChild(placementLabel);

        statusLabel = new Label { Text = "", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        statusLabel.AddThemeFontSizeOverride("font_size", 14);
        root.AddChild(statusLabel);

        validationLabel = new Label { Text = "", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        validationLabel.AddThemeFontSizeOverride("font_size", 14);
        root.AddChild(validationLabel);

        root.AddChild(new HSeparator());

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        root.AddChild(scroll);

        var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(body);

        BuildIdentitySection(body);
        BuildStatsSection(body);
        BuildLootSection(body);
    }

    private void BuildIdentitySection(Node parent)
    {
        AddHeading(parent, "Identity & mesh");
        var grid = AddGrid(parent);

        AddText(grid, "NPC id", MutedColor);
        idEdit = new LineEdit { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        idEdit.TextChanged += text =>
        {
            if (!suppressSignals)
            {
                Model.NpcId = text;
                RefreshValidation();
            }
        };
        grid.AddChild(idEdit);

        AddText(grid, "Display name", MutedColor);
        nameEdit = new LineEdit { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        nameEdit.TextChanged += text =>
        {
            if (!suppressSignals)
            {
                Model.DisplayName = text;
                RefreshValidation();
            }
        };
        grid.AddChild(nameEdit);

        AddText(grid, "Mesh / rig", MutedColor);
        rigPicker = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        rigPicker.ItemSelected += OnRigSelected;
        grid.AddChild(rigPicker);

        AddText(grid, "Facing (°)", MutedColor);
        rotationSpin = new SpinBox { MinValue = -180, MaxValue = 180, Step = 5 };
        rotationSpin.ValueChanged += value =>
        {
            if (!suppressSignals)
            {
                Model.RotationDegreesY = (float)value;
                FacingChanged?.Invoke(Model.RotationDegreesY);
            }
        };
        grid.AddChild(rotationSpin);
    }

    private void BuildStatsSection(Node parent)
    {
        AddHeading(parent, "Character sheet");
        var grid = AddGrid(parent);
        AddStatSpin(grid, "Level", 1, 99, () => Model.Level, v => Model.Level = v);
        AddStatSpin(grid, "Max HP", 1, 9999, () => Model.MaxHealthPoints, v => Model.MaxHealthPoints = v);
        AddStatSpin(grid, "Max PP", 0, 9999, () => Model.MaxMagicPoints, v => Model.MaxMagicPoints = v);
        AddStatSpin(grid, "Strength", 0, 99, () => Model.Stats.Strength, v => Model.Stats.Strength = v);
        AddStatSpin(grid, "Intelligence", 0, 99, () => Model.Stats.Intelligence, v => Model.Stats.Intelligence = v);
        AddStatSpin(grid, "Constitution", 0, 99, () => Model.Stats.Constitution, v => Model.Stats.Constitution = v);
        AddStatSpin(grid, "Dexterity", 0, 99, () => Model.Stats.Dexterity, v => Model.Stats.Dexterity = v);
        AddStatSpin(grid, "Wisdom", 0, 99, () => Model.Stats.Wisdom, v => Model.Stats.Wisdom = v);
        AddStatSpin(grid, "Charisma", 0, 99, () => Model.Stats.Charisma, v => Model.Stats.Charisma = v);
    }

    private void BuildLootSection(Node parent)
    {
        AddHeading(parent, "Credits & starting items");
        var grid = AddGrid(parent);
        AddText(grid, "Credits", MutedColor);
        creditsSpin = new SpinBox { MinValue = 0, MaxValue = 999999, Step = 10 };
        creditsSpin.ValueChanged += value =>
        {
            if (!suppressSignals)
            {
                Model.Credits = (uint)value;
            }
        };
        grid.AddChild(creditsSpin);

        itemRows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        itemRows.AddThemeConstantOverride("separation", 3);
        parent.AddChild(itemRows);

        var addRow = new HBoxContainer();
        addRow.AddThemeConstantOverride("separation", 6);
        parent.AddChild(addRow);

        addItemPicker = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var item in ItemCatalog.All.OrderBy(i => i.Id))
        {
            catalogItemIds.Add(item.Id);
            addItemPicker.AddItem($"{item.Name}  (#{item.Id})");
        }
        addRow.AddChild(addItemPicker);

        var add = new Button { Text = "+ item", FocusMode = Control.FocusModeEnum.None };
        add.Pressed += OnAddItem;
        addRow.AddChild(add);
    }

    private void AddStatSpin(Node grid, string label, int min, int max, Func<uint> get, Action<uint> set)
    {
        AddText(grid, label, MutedColor);
        var spin = new SpinBox { MinValue = min, MaxValue = max, Value = get() };
        spin.ValueChanged += value =>
        {
            if (!suppressSignals)
            {
                set((uint)value);
                RefreshValidation();
            }
        };
        grid.AddChild(spin);
        statSpins[label] = spin;
    }

    private static GridContainer AddGrid(Node parent)
    {
        var grid = new GridContainer
        {
            Columns = 2,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 4);
        parent.AddChild(grid);
        return grid;
    }

    private static void AddHeading(Node parent, string text)
    {
        parent.AddChild(new HSeparator());
        var label = new Label { Text = text };
        label.AddThemeColorOverride("font_color", HeadingColor);
        label.AddThemeFontSizeOverride("font_size", 16);
        parent.AddChild(label);
    }

    private static void AddText(Node parent, string text, Color color, int fontSize = 15)
    {
        var label = new Label { Text = text };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        parent.AddChild(label);
    }
}
