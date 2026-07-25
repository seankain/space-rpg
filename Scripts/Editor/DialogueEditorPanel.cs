using Godot;
using System;
using System.Collections.Generic;

// The in-editor dialogue viewer (dialogue-editor plan Phase 3): a node list on
// the left, a detail panel on the right. Read-only for now — it walks the
// loaded DialogueGraph so an author can see every node's speaker, text,
// effects, conditions, and link targets, and click a link to jump to its target
// node. Editing and saving are Phase 4.
//
// Code-built CanvasLayer like the other editor UI (DevConsole's HUD/cursor).
// DevConsole owns its lifecycle — it creates this on first `dialogue open`,
// shows/hides it, and manages the mouse and gameplay lock — so this class only
// renders a graph and reports a close request through Closed.
public partial class DialogueEditorPanel : CanvasLayer
{
	// Raised when the panel asks to close (Close button or Escape). DevConsole
	// hides the panel and restores play/mouse.
	public Action Closed { get; set; }

	private DialogueGraph graph;
	private string selectedNodeId;

	private Label titleLabel;
	private OptionButton picker;
	private VBoxContainer nodeList;
	private VBoxContainer detail;
	private readonly Dictionary<string, Button> nodeButtons = new();
	private readonly List<string> pickerIds = new();
	// Guards the picker's item_selected handler while we repopulate it in code.
	private bool suppressPicker;

	private static readonly Color EntryColor = new(0.5f, 1f, 0.6f);
	private static readonly Color RouterColor = new(0.7f, 0.8f, 1f);
	private static readonly Color SelectedColor = new(1f, 0.9f, 0.5f);
	private static readonly Color LinkColor = new(0.6f, 0.75f, 0.95f);
	private static readonly Color MutedColor = new(0.65f, 0.67f, 0.7f);
	private static readonly Color HeadingColor = new(1f, 0.85f, 0.4f);

	public override void _Ready()
	{
		Layer = 90; // above the dialogue box (50), below the console (100)
		Visible = false;
		BuildUi();
	}

	// True while the panel is showing a graph — DevConsole folds this into its
	// gameplay lock.
	public bool IsShowing => Visible;

	// Load a graph into the panel: title, node list, and detail for the entry
	// node. Does not touch Visible or the mouse (DevConsole handles those).
	public void ShowGraph(DialogueGraph graph)
	{
		this.graph = graph;
		titleLabel.Text = $"Dialogue:  {graph.Id}";
		RefreshPicker();
		BuildNodeList();
		SelectNode(graph.EntryNodeId);
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
		foreach (var id in SortedCatalogIds())
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

	private static List<string> SortedCatalogIds()
	{
		var ids = new List<string>(DialogueCatalog.Ids);
		ids.Sort(StringComparer.OrdinalIgnoreCase);
		return ids;
	}

	private void OnPickerSelected(long index)
	{
		if (suppressPicker || index < 0 || index >= pickerIds.Count)
		{
			return;
		}
		if (DialogueCatalog.Get(pickerIds[(int)index]) is { } picked)
		{
			ShowGraph(picked);
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
			var isRouter = node.Branches is { Count: > 0 };
			var label = node.Id;
			if (isEntry)
			{
				label = "▶ " + label;
			}
			if (isRouter)
			{
				label += "  (router)";
			}
			var button = new Button
			{
				Text = label,
				Alignment = HorizontalAlignment.Left,
				ToggleMode = false,
			};
			var id = node.Id;
			button.Pressed += () => SelectNode(id);
			nodeList.AddChild(button);
			nodeButtons[node.Id] = button;
		}
	}

	private void HighlightSelected()
	{
		foreach (var (id, button) in nodeButtons)
		{
			var node = graph.GetNode(id);
			Color color;
			if (id == selectedNodeId)
			{
				color = SelectedColor;
			}
			else if (id == graph.EntryNodeId)
			{
				color = EntryColor;
			}
			else if (node?.Branches is { Count: > 0 })
			{
				color = RouterColor;
			}
			else
			{
				color = Colors.White;
			}
			button.AddThemeColorOverride("font_color", color);
			button.AddThemeColorOverride("font_hover_color", color);
		}
	}

	// --- Detail --------------------------------------------------------------

	private void SelectNode(string nodeId)
	{
		selectedNodeId = nodeId;
		HighlightSelected();
		BuildDetail(nodeId);
	}

	private void BuildDetail(string nodeId)
	{
		foreach (var child in detail.GetChildren())
		{
			child.QueueFree();
		}
		var node = graph.GetNode(nodeId);
		if (node == null)
		{
			AddText(detail, $"Node '{nodeId}' does not exist.", MutedColor);
			return;
		}

		var heading = node.Id == graph.EntryNodeId ? $"{node.Id}   (entry)" : node.Id;
		AddText(detail, heading, HeadingColor, 22);

		if (node.Branches is { Count: > 0 })
		{
			BuildRouterDetail(node);
			return;
		}

		AddField("Speaker", SpeakerDisplay(node.Speaker));
		AddText(detail, "Text", MutedColor, 14);
		AddText(detail, string.IsNullOrEmpty(node.Text) ? "(none)" : node.Text, Colors.White, 18, wrap: true);

		if (node.OnShownEffects is { Count: > 0 })
		{
			AddText(detail, "On shown", MutedColor, 14);
			foreach (var effect in node.OnShownEffects)
			{
				AddText(detail, "  • " + effect.ToToken(), Colors.White);
			}
		}

		if (node.Choices is { Count: > 0 })
		{
			AddText(detail, "Choices", MutedColor, 14);
			foreach (var choice in node.Choices)
			{
				BuildChoiceDetail(choice);
			}
		}
		else
		{
			AddLinkRow("Next", node.NextNodeId);
		}
	}

	private void BuildRouterDetail(DialogueNode node)
	{
		AddText(detail, "Router — takes the first branch whose condition holds.", MutedColor, 14, wrap: true);
		foreach (var branch in node.Branches)
		{
			var when = branch.When == null ? "always" : "when " + branch.When.ToToken();
			AddLinkRow(when + "  →", branch.ToNodeId);
		}
		if (!string.IsNullOrEmpty(node.NextNodeId))
		{
			AddLinkRow("otherwise  →", node.NextNodeId);
		}
	}

	private void BuildChoiceDetail(DialogueChoiceData choice)
	{
		var block = new PanelContainer();
		var style = new StyleBoxFlat
		{
			BgColor = new Color(1f, 1f, 1f, 0.04f),
			ContentMarginLeft = 10,
			ContentMarginRight = 10,
			ContentMarginTop = 6,
			ContentMarginBottom = 6,
		};
		block.AddThemeStyleboxOverride("panel", style);
		detail.AddChild(block);

		var column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 2);
		block.AddChild(column);

		AddText(column, string.IsNullOrEmpty(choice.Label) ? "(no label)" : choice.Label, Colors.White, 18, wrap: true);
		if (choice.Visible != null)
		{
			AddText(column, "visible when  " + choice.Visible.ToToken(), MutedColor);
		}
		if (choice.Effects is { Count: > 0 })
		{
			foreach (var effect in choice.Effects)
			{
				AddText(column, "does  " + effect.ToToken(), MutedColor);
			}
		}
		AddLinkRow("→", choice.NextNodeId, column);
	}

	// --- Small builders ------------------------------------------------------

	private void AddField(string label, string value)
	{
		AddText(detail, label, MutedColor, 14);
		AddText(detail, value, Colors.White, 18, wrap: true);
	}

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

	// A "prefix  → target" row whose target is a button that jumps to that node.
	// An empty target ends the conversation; an unknown target is flagged.
	private void AddLinkRow(string prefix, string targetId, Node parent = null)
	{
		parent ??= detail;
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		parent.AddChild(row);

		var prefixLabel = new Label { Text = prefix };
		prefixLabel.AddThemeColorOverride("font_color", MutedColor);
		row.AddChild(prefixLabel);

		if (string.IsNullOrEmpty(targetId))
		{
			var ends = new Label { Text = "(ends conversation)" };
			ends.AddThemeColorOverride("font_color", MutedColor);
			row.AddChild(ends);
			return;
		}

		var known = graph.GetNode(targetId) != null;
		var link = new Button
		{
			Text = known ? targetId : $"{targetId} (unknown)",
			Flat = true,
			Disabled = !known,
		};
		link.AddThemeColorOverride("font_color", known ? LinkColor : new Color(1f, 0.5f, 0.45f));
		link.AddThemeColorOverride("font_hover_color", Colors.White);
		if (known)
		{
			var id = targetId;
			link.Pressed += () => SelectNode(id);
		}
		row.AddChild(link);
	}

	// --- Layout --------------------------------------------------------------

	private string SpeakerDisplay(string speaker)
	{
		if (speaker == DialogueGraph.SpeakerToken)
		{
			return "$npc  (the speaking NPC)";
		}
		return string.IsNullOrEmpty(speaker) ? "(narration)" : speaker;
	}

	private void BuildUi()
	{
		var panel = new PanelContainer
		{
			AnchorLeft = 0.04f,
			AnchorRight = 0.96f,
			AnchorTop = 0.06f,
			AnchorBottom = 0.94f,
		};
		panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.06f, 0.07f, 0.09f, 0.96f) });
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

		// Header: title, dialogue picker, close.
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

		var close = new Button { Text = "Close  (Esc)" };
		close.Pressed += () => Closed?.Invoke();
		header.AddChild(close);

		root.AddChild(new HSeparator());

		// Body: node list | detail.
		var body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		body.AddThemeConstantOverride("separation", 12);
		root.AddChild(body);

		var leftColumn = new VBoxContainer { CustomMinimumSize = new Vector2(240, 0) };
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
