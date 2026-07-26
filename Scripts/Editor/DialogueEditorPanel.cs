using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// The in-editor dialogue editor (dialogue-editor plan Phases 3-4): a node list
// on the left, an editable detail panel on the right. The author edits a
// working copy of the graph (DevConsole hands it a clone, or a fresh graph for
// `dialogue new`) — rewriting speaker/text, adding/removing/reordering choices,
// setting targets from a node dropdown, and picking effects/conditions from the
// fixed vocabulary. A validation strip updates on every edit; `dialogue save`
// (or the Save button) writes it back through DevConsole/DialogueEditing.
//
// Code-built CanvasLayer like the other editor UI. DevConsole owns the panel's
// lifecycle, mouse, and gameplay lock and performs the file I/O; this class
// renders and mutates the graph and reports open/save/close through callbacks.
public partial class DialogueEditorPanel : CanvasLayer
{
	// Raised when the panel asks to close (Close button or Escape).
	public Action Closed { get; set; }
	// Raised by the Save button; DevConsole validates and writes the file.
	public Action SaveRequested { get; set; }
	// Raised by the dialogue picker; DevConsole reopens that id as a fresh
	// working copy (so switching doesn't mutate the cached catalog graph).
	public Action<string> OpenRequested { get; set; }

	// Raised by "Play from here"; DevConsole runs the working graph through
	// DialogueManager from the given node (null = the selected node / entry).
	public Action<string> PlayRequested { get; set; }

	// The working copy being edited (already a clone / fresh graph).
	public DialogueGraph WorkingGraph => graph;

	// The node the detail panel is showing — the "here" of "play from here".
	public string SelectedNodeId => selectedNodeId;

	// Editor-only positions from the graph canvas, or null if it was never
	// opened; DevConsole saves it beside the dialogue on save.
	public DialogueLayout CaptureLayout() => canvas?.CaptureLayout(graph?.Id);

	private DialogueGraph graph;
	private string selectedNodeId;

	private Label titleLabel;
	private OptionButton picker;
	private RichTextLabel validationLabel;
	private Label statusLabel;
	private VBoxContainer leftColumn;
	private VBoxContainer nodeList;
	private DialogueGraphCanvas canvas;
	private Button graphViewButton;
	private bool graphViewActive;
	private VBoxContainer detail;
	private readonly Dictionary<string, Button> nodeButtons = new();
	private readonly List<string> pickerIds = new();
	private bool suppressPicker;

	private static readonly Color EntryColor = new(0.5f, 1f, 0.6f);
	private static readonly Color RouterColor = new(0.7f, 0.8f, 1f);
	private static readonly Color SelectedColor = new(1f, 0.9f, 0.5f);
	private static readonly Color MutedColor = new(0.65f, 0.67f, 0.7f);
	private static readonly Color HeadingColor = new(1f, 0.85f, 0.4f);
	private static readonly Color ErrorColor = new(1f, 0.5f, 0.45f);
	private static readonly Color WarnColor = new(1f, 0.82f, 0.4f);
	private static readonly Color OkColor = new(0.55f, 0.85f, 0.6f);

	private const string EndsLabel = "(ends conversation)";
	private const string NoConditionLabel = "(always)";

	public override void _Ready()
	{
		Layer = 90; // above the dialogue box (50), below the console (100)
		Visible = false;
		BuildChrome();
	}

	public bool IsShowing => Visible;

	// Load a working graph: title, picker, node list, and the entry node's
	// detail. Does not touch Visible or the mouse (DevConsole handles those).
	public void ShowGraph(DialogueGraph graph)
	{
		this.graph = graph;
		titleLabel.Text = $"Editing:  {graph.Id}";
		SetStatus("", true);
		RefreshPicker();
		BuildNodeList();
		SelectNode(graph.EntryNodeId);
		RefreshValidation();
		if (graphViewActive)
		{
			RepopulateCanvas();
		}
	}

	private void RepopulateCanvas() => canvas.Populate(graph, DialogueEditing.LoadLayout(graph.Id));

	private void ToggleGraphView()
	{
		graphViewActive = !graphViewActive;
		graphViewButton.Text = graphViewActive ? "List view" : "Graph view";
		leftColumn.Visible = !graphViewActive;
		canvas.Visible = graphViewActive;
		if (graphViewActive)
		{
			RepopulateCanvas();
		}
	}

	public void SetStatus(string text, bool ok)
	{
		statusLabel.Text = text ?? "";
		statusLabel.AddThemeColorOverride("font_color", ok ? OkColor : ErrorColor);
	}

	// After a save: reflect a possible rename in the title and add the now-
	// cataloged id to the picker, without disturbing the current selection.
	public void OnSaved()
	{
		titleLabel.Text = $"Editing:  {graph.Id}";
		RefreshPicker();
		RefreshValidation();
	}

	public override void _Input(InputEvent @event)
	{
		if (Visible && @event.IsActionPressed("ui_cancel"))
		{
			GetViewport().SetInputAsHandled();
			Closed?.Invoke();
		}
	}

	// --- Picker --------------------------------------------------------------

	private void RefreshPicker()
	{
		suppressPicker = true;
		picker.Clear();
		pickerIds.Clear();
		var ids = new List<string>(DialogueCatalog.Ids);
		ids.Sort(StringComparer.OrdinalIgnoreCase);
		// A brand-new (unsaved) graph won't be in the catalog yet; list it too.
		if (!ids.Contains(graph.Id))
		{
			ids.Insert(0, graph.Id);
		}
		foreach (var id in ids)
		{
			pickerIds.Add(id);
			picker.AddItem(id);
			if (id == graph.Id)
			{
				picker.Select(pickerIds.Count - 1);
			}
		}
		suppressPicker = false;
	}

	private void OnPickerSelected(long index)
	{
		if (suppressPicker || index < 0 || index >= pickerIds.Count)
		{
			return;
		}
		var id = pickerIds[(int)index];
		if (id != graph.Id)
		{
			OpenRequested?.Invoke(id);
		}
	}

	// --- Node list -----------------------------------------------------------

	private void BuildNodeList()
	{
		foreach (var child in nodeList.GetChildren())
		{
			child.QueueFree();
		}
		nodeButtons.Clear();
		foreach (var node in graph.Nodes)
		{
			var isEntry = node.Id == graph.EntryNodeId;
			var isRouter = node.Branches != null;
			var label = (isEntry ? "▶ " : "") + node.Id + (isRouter ? "  (router)" : "");
			var button = new Button { Text = label, Alignment = HorizontalAlignment.Left };
			var id = node.Id;
			button.Pressed += () => SelectNode(id);
			nodeList.AddChild(button);
			nodeButtons[node.Id] = button;
		}
		HighlightSelected();
	}

	private void HighlightSelected()
	{
		foreach (var (id, button) in nodeButtons)
		{
			var node = graph.GetNode(id);
			var color = id == selectedNodeId ? SelectedColor
				: id == graph.EntryNodeId ? EntryColor
				: node?.Branches != null ? RouterColor
				: Colors.White;
			button.AddThemeColorOverride("font_color", color);
			button.AddThemeColorOverride("font_hover_color", color);
		}
	}

	private void AddNode(bool router)
	{
		var node = DialogueGraphEditing.AddNode(graph, router);
		BuildNodeList();
		SelectNode(node.Id);
		RefreshValidation();
	}

	// --- Detail --------------------------------------------------------------

	private void SelectNode(string nodeId)
	{
		selectedNodeId = nodeId;
		HighlightSelected();
		BuildDetail();
	}

	private void RebuildDetail() => BuildDetail();

	private void BuildDetail()
	{
		foreach (var child in detail.GetChildren())
		{
			child.QueueFree();
		}
		var node = graph.GetNode(selectedNodeId);
		if (node == null)
		{
			AddText(detail, selectedNodeId == null ? "Select a node." : $"Node '{selectedNodeId}' is gone.", MutedColor);
			return;
		}

		BuildNodeHeader(node);
		if (node.Branches is { Count: > 0 } || IsRouterShaped(node))
		{
			BuildRouterEditor(node);
		}
		else
		{
			BuildLineEditor(node);
		}
	}

	// A node with an empty Branches list (a freshly added router) still edits as
	// a router even though Count is 0.
	private static bool IsRouterShaped(DialogueNode node) => node.Branches != null;

	private void BuildNodeHeader(DialogueNode node)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		detail.AddChild(row);

		AddText(row, "id", MutedColor);
		var idEdit = new LineEdit { Text = node.Id, CustomMinimumSize = new Vector2(160, 0) };
		idEdit.TextSubmitted += newId => RenameSelected(newId, idEdit);
		row.AddChild(idEdit);

		if (node.Id == graph.EntryNodeId)
		{
			AddText(row, "(entry)", EntryColor);
		}
		else
		{
			var makeEntry = new Button { Text = "Make entry" };
			makeEntry.Pressed += () =>
			{
				graph.EntryNodeId = node.Id;
				BuildNodeList();
				RefreshValidation();
			};
			row.AddChild(makeEntry);
		}

		var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		row.AddChild(spacer);

		var delete = new Button { Text = "Delete node" };
		delete.AddThemeColorOverride("font_color", ErrorColor);
		delete.Pressed += () =>
		{
			DialogueGraphEditing.DeleteNode(graph, node.Id);
			BuildNodeList();
			SelectNode(graph.GetNode(graph.EntryNodeId)?.Id ?? graph.Nodes.FirstOrDefault()?.Id);
			RefreshValidation();
		};
		row.AddChild(delete);

		detail.AddChild(new HSeparator());
	}

	private void RenameSelected(string newId, LineEdit idEdit)
	{
		var old = selectedNodeId;
		if (DialogueGraphEditing.RenameNode(graph, old, newId))
		{
			BuildNodeList();
			SelectNode(newId);
			RefreshValidation();
			SetStatus($"Renamed '{old}' to '{newId}'.", true);
		}
		else
		{
			idEdit.Text = old;
			SetStatus($"Can't rename to '{newId}' — empty or already used.", false);
		}
	}

	private void BuildLineEditor(DialogueNode node)
	{
		AddText(detail, "Speaker  ($npc = the speaking NPC)", MutedColor, 14);
		AddLineEdit(detail, node.Speaker, v => node.Speaker = v);

		AddText(detail, "Text", MutedColor, 14);
		AddTextEdit(detail, node.Text, v => node.Text = v);

		AddEffectsEditor(detail, "On shown, do:", () => node.OnShownEffects, v => node.OnShownEffects = v);

		AddText(detail, "Choices", MutedColor, 14);
		if (node.Choices is { Count: > 0 })
		{
			for (var i = 0; i < node.Choices.Count; i++)
			{
				BuildChoiceEditor(node, i);
			}
		}
		else
		{
			AddText(detail, "  (none — this line continues to Next)", MutedColor);
		}
		var addChoice = new Button { Text = "+ choice" };
		addChoice.Pressed += () =>
		{
			node.Choices ??= new List<DialogueChoiceData>();
			node.Choices.Add(new DialogueChoiceData { Label = "New choice" });
			RebuildDetail();
			RefreshValidation();
		};
		detail.AddChild(addChoice);

		detail.AddChild(new HSeparator());
		var hint = node.Choices is { Count: > 0 } ? "Next  (ignored while choices exist)" : "Next";
		AddTargetEditor(detail, hint, () => node.NextNodeId, v => node.NextNodeId = v);
	}

	private void BuildChoiceEditor(DialogueNode node, int index)
	{
		var choice = node.Choices[index];
		var block = new PanelContainer();
		block.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(1f, 1f, 1f, 0.05f),
			ContentMarginLeft = 10,
			ContentMarginRight = 10,
			ContentMarginTop = 6,
			ContentMarginBottom = 6,
		});
		detail.AddChild(block);

		var column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 3);
		block.AddChild(column);

		var top = new HBoxContainer();
		top.AddThemeConstantOverride("separation", 6);
		column.AddChild(top);
		AddText(top, $"#{index + 1}", MutedColor);
		var label = new LineEdit { Text = choice.Label ?? "", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		label.TextChanged += t => { choice.Label = t; RefreshValidation(); };
		top.AddChild(label);
		AddReorderAndDelete(top, node.Choices, index, () => { if (node.Choices.Count == 0) node.Choices = null; });

		AddConditionEditor(column, "visible when", () => choice.Visible, v => choice.Visible = v);
		AddEffectsEditor(column, "does:", () => choice.Effects, v => choice.Effects = v);
		AddTargetEditor(column, "goes to", () => choice.NextNodeId, v => choice.NextNodeId = v);
	}

	private void BuildRouterEditor(DialogueNode node)
	{
		AddText(detail, "Router — takes the first branch whose condition holds.", MutedColor, 14, wrap: true);
		node.Branches ??= new List<DialogueBranch>();
		for (var i = 0; i < node.Branches.Count; i++)
		{
			BuildBranchEditor(node, i);
		}
		var addBranch = new Button { Text = "+ branch" };
		addBranch.Pressed += () =>
		{
			node.Branches.Add(new DialogueBranch());
			RebuildDetail();
			RefreshValidation();
		};
		detail.AddChild(addBranch);

		detail.AddChild(new HSeparator());
		AddTargetEditor(detail, "otherwise (fallback)", () => node.NextNodeId, v => node.NextNodeId = v);
	}

	private void BuildBranchEditor(DialogueNode node, int index)
	{
		var branch = node.Branches[index];
		var block = new PanelContainer();
		block.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(1f, 1f, 1f, 0.05f),
			ContentMarginLeft = 10,
			ContentMarginRight = 10,
			ContentMarginTop = 6,
			ContentMarginBottom = 6,
		});
		detail.AddChild(block);

		var column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 3);
		block.AddChild(column);

		var top = new HBoxContainer();
		top.AddThemeConstantOverride("separation", 6);
		column.AddChild(top);
		AddText(top, $"#{index + 1}", MutedColor);
		var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		top.AddChild(spacer);
		AddReorderAndDelete(top, node.Branches, index, null);

		AddConditionEditor(column, "when", () => branch.When, v => branch.When = v);
		AddTargetEditor(column, "goes to", () => branch.ToNodeId, v => branch.ToNodeId = v);
	}

	// --- Reusable editors ----------------------------------------------------

	private void AddReorderAndDelete<T>(Node parent, List<T> list, int index, Action afterDelete)
	{
		var up = new Button { Text = "↑", Disabled = index == 0 };
		up.Pressed += () => { DialogueGraphEditing.Move(list, index, -1); RebuildDetail(); RefreshValidation(); };
		parent.AddChild(up);

		var down = new Button { Text = "↓", Disabled = index >= list.Count - 1 };
		down.Pressed += () => { DialogueGraphEditing.Move(list, index, 1); RebuildDetail(); RefreshValidation(); };
		parent.AddChild(down);

		var del = new Button { Text = "✕" };
		del.AddThemeColorOverride("font_color", ErrorColor);
		del.Pressed += () =>
		{
			list.RemoveAt(index);
			afterDelete?.Invoke();
			RebuildDetail();
			RefreshValidation();
		};
		parent.AddChild(del);
	}

	private void AddLineEdit(Node parent, string value, Action<string> onChanged)
	{
		var edit = new LineEdit { Text = value ?? "", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		edit.TextChanged += t => { onChanged(t); RefreshValidation(); };
		parent.AddChild(edit);
	}

	private void AddTextEdit(Node parent, string value, Action<string> onChanged)
	{
		var edit = new TextEdit
		{
			Text = value ?? "",
			WrapMode = TextEdit.LineWrappingMode.Boundary,
			CustomMinimumSize = new Vector2(0, 76),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		edit.TextChanged += () => { onChanged(edit.Text); RefreshValidation(); };
		parent.AddChild(edit);
	}

	// A "label [ (ends) | node... ]" dropdown that sets a node-id link (empty =
	// ends conversation). A dangling current target is shown so it can be fixed.
	private void AddTargetEditor(Node parent, string label, Func<string> get, Action<string> set)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		parent.AddChild(row);
		AddText(row, label, MutedColor);

		var dropdown = new OptionButton();
		var values = new List<string> { null };
		dropdown.AddItem(EndsLabel);
		foreach (var node in graph.Nodes)
		{
			dropdown.AddItem(node.Id);
			values.Add(node.Id);
		}
		var current = get();
		var selected = 0;
		if (!string.IsNullOrEmpty(current))
		{
			var found = values.IndexOf(current);
			if (found < 0)
			{
				dropdown.AddItem($"{current} (missing)");
				values.Add(current);
				found = values.Count - 1;
			}
			selected = found;
		}
		dropdown.Select(selected);
		dropdown.ItemSelected += index => { set(values[(int)index]); RefreshValidation(); };
		row.AddChild(dropdown);
	}

	// Verb dropdown + colon-joined args field for a single optional condition
	// (a choice's visibility or a branch's guard). "(always)" clears it.
	private void AddConditionEditor(Node parent, string label, Func<ConditionRef> get, Action<ConditionRef> set)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		parent.AddChild(row);
		AddText(row, label, MutedColor);

		var verb = new OptionButton();
		verb.AddItem(NoConditionLabel);
		foreach (var id in DialogueConditions.Ids)
		{
			verb.AddItem(id);
		}
		var current = get();
		verb.Select(current == null ? 0 : Math.Max(0, Array.IndexOf(DialogueConditions.Ids, current.Id) + 1));
		row.AddChild(verb);

		var args = new LineEdit
		{
			PlaceholderText = "args e.g. 1:Success",
			Text = current?.Args is { Length: > 0 } a ? string.Join(":", a) : "",
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		row.AddChild(args);

		verb.ItemSelected += index =>
		{
			if (index <= 0)
			{
				set(null);
			}
			else
			{
				var c = get() ?? new ConditionRef();
				c.Id = DialogueConditions.Ids[(int)index - 1];
				c.Args = SplitArgs(args.Text);
				set(c);
			}
			RefreshValidation();
		};
		args.TextChanged += t =>
		{
			if (get() is { } c)
			{
				c.Args = SplitArgs(t);
				RefreshValidation();
			}
		};
	}

	// An editable list of effects (a node's OnShown or a choice's effects): each
	// row is a verb dropdown + args field + remove; a "+ effect" button appends.
	private void AddEffectsEditor(Node parent, string heading, Func<List<EffectRef>> get, Action<List<EffectRef>> set)
	{
		AddText(parent, heading, MutedColor, 14);
		var list = get();
		if (list != null)
		{
			for (var i = 0; i < list.Count; i++)
			{
				BuildEffectRow(parent, list, i, set);
			}
		}
		var add = new Button { Text = "+ effect" };
		add.Pressed += () =>
		{
			var l = get() ?? new List<EffectRef>();
			l.Add(new EffectRef { Id = DialogueEffects.Ids[0], Args = Array.Empty<string>() });
			set(l);
			RebuildDetail();
			RefreshValidation();
		};
		parent.AddChild(add);
	}

	private void BuildEffectRow(Node parent, List<EffectRef> list, int index, Action<List<EffectRef>> set)
	{
		var effect = list[index];
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 6);
		parent.AddChild(row);

		var verb = new OptionButton();
		foreach (var id in DialogueEffects.Ids)
		{
			verb.AddItem(id);
		}
		verb.Select(Math.Max(0, Array.IndexOf(DialogueEffects.Ids, effect.Id)));
		verb.ItemSelected += i => { effect.Id = DialogueEffects.Ids[(int)i]; RefreshValidation(); };
		row.AddChild(verb);

		var args = new LineEdit
		{
			PlaceholderText = "args",
			Text = effect.Args is { Length: > 0 } a ? string.Join(":", a) : "",
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		args.TextChanged += t => { effect.Args = SplitArgs(t); RefreshValidation(); };
		row.AddChild(args);

		var del = new Button { Text = "✕" };
		del.AddThemeColorOverride("font_color", ErrorColor);
		del.Pressed += () =>
		{
			list.RemoveAt(index);
			if (list.Count == 0)
			{
				set(null);
			}
			RebuildDetail();
			RefreshValidation();
		};
		row.AddChild(del);
	}

	private static string[] SplitArgs(string text) =>
		string.IsNullOrEmpty(text) ? Array.Empty<string>() : text.Split(':');

	private static void AddText(Node parent, string text, Color color, int fontSize = 16, bool wrap = false)
	{
		var label = new Label { Text = text };
		if (wrap)
		{
			label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		}
		label.AddThemeColorOverride("font_color", color);
		label.AddThemeFontSizeOverride("font_size", fontSize);
		parent.AddChild(label);
	}

	// --- Validation ----------------------------------------------------------

	private void RefreshValidation()
	{
		var issues = DialogueValidation.Validate(graph);
		validationLabel.Clear();
		if (issues.Count == 0)
		{
			validationLabel.PushColor(OkColor);
			validationLabel.AddText("No problems.");
			validationLabel.Pop();
			return;
		}
		var errors = issues.Count(i => i.Severity == DialogueSeverity.Error);
		var warns = issues.Count - errors;
		validationLabel.PushColor(errors > 0 ? ErrorColor : WarnColor);
		validationLabel.AddText($"{errors} error(s), {warns} warning(s)");
		validationLabel.Pop();
		validationLabel.Newline();
		foreach (var issue in issues.Take(8))
		{
			validationLabel.PushColor(issue.Severity == DialogueSeverity.Error ? ErrorColor : WarnColor);
			validationLabel.AddText($"  • {issue.Message}");
			validationLabel.Pop();
			validationLabel.Newline();
		}
	}

	// --- Chrome --------------------------------------------------------------

	private void BuildChrome()
	{
		var panel = new PanelContainer
		{
			AnchorLeft = 0.03f,
			AnchorRight = 0.97f,
			AnchorTop = 0.05f,
			AnchorBottom = 0.95f,
		};
		panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.06f, 0.07f, 0.09f, 0.97f) });
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
		header.AddThemeConstantOverride("separation", 12);
		root.AddChild(header);

		titleLabel = new Label { Text = "Dialogue", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		titleLabel.AddThemeFontSizeOverride("font_size", 22);
		titleLabel.AddThemeColorOverride("font_color", HeadingColor);
		header.AddChild(titleLabel);

		var pickerLabel = new Label { Text = "Open:" };
		pickerLabel.AddThemeColorOverride("font_color", MutedColor);
		header.AddChild(pickerLabel);

		picker = new OptionButton();
		picker.ItemSelected += OnPickerSelected;
		header.AddChild(picker);

		graphViewButton = new Button { Text = "Graph view" };
		graphViewButton.Pressed += ToggleGraphView;
		header.AddChild(graphViewButton);

		var play = new Button { Text = "▶ Play from here" };
		play.Pressed += () => PlayRequested?.Invoke(selectedNodeId);
		header.AddChild(play);

		var save = new Button { Text = "Save" };
		save.Pressed += () => SaveRequested?.Invoke();
		header.AddChild(save);

		var close = new Button { Text = "Close  (Esc)" };
		close.Pressed += () => Closed?.Invoke();
		header.AddChild(close);

		statusLabel = new Label { Text = "" };
		statusLabel.AddThemeFontSizeOverride("font_size", 14);
		root.AddChild(statusLabel);

		validationLabel = new RichTextLabel
		{
			FitContent = true,
			ScrollActive = false,
			CustomMinimumSize = new Vector2(0, 24),
			SelectionEnabled = false,
			FocusMode = Control.FocusModeEnum.None,
		};
		validationLabel.AddThemeFontSizeOverride("normal_font_size", 14);
		root.AddChild(validationLabel);

		root.AddChild(new HSeparator());

		var body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		body.AddThemeConstantOverride("separation", 12);
		root.AddChild(body);

		leftColumn = new VBoxContainer { CustomMinimumSize = new Vector2(240, 0) };
		body.AddChild(leftColumn);

		var nodesHeading = new Label { Text = "Nodes" };
		nodesHeading.AddThemeColorOverride("font_color", MutedColor);
		leftColumn.AddChild(nodesHeading);

		var listScroll = new ScrollContainer
		{
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		leftColumn.AddChild(listScroll);

		nodeList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		nodeList.AddThemeConstantOverride("separation", 2);
		listScroll.AddChild(nodeList);

		var addRow = new HBoxContainer();
		addRow.AddThemeConstantOverride("separation", 6);
		leftColumn.AddChild(addRow);
		var addNode = new Button { Text = "+ node", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		addNode.Pressed += () => AddNode(router: false);
		addRow.AddChild(addNode);
		var addRouter = new Button { Text = "+ router", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		addRouter.Pressed += () => AddNode(router: true);
		addRow.AddChild(addRouter);

		// The optional graph canvas shares the left area with the node list,
		// swapped in by the "Graph view" toggle. Wider than the list, so it
		// takes the larger share of the body when shown.
		canvas = new DialogueGraphCanvas
		{
			Visible = false,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			SizeFlagsStretchRatio = 2f,
		};
		canvas.NodeClicked = id => SelectNode(id);
		body.AddChild(canvas);

		body.AddChild(new VSeparator());

		var detailScroll = new ScrollContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		body.AddChild(detailScroll);

		detail = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		detail.AddThemeConstantOverride("separation", 5);
		detailScroll.AddChild(detail);
	}
}
