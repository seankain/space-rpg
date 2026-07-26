using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// The optional node-graph view for the dialogue editor (dialogue-editor plan
// Phase 5): a Godot GraphEdit showing each dialogue node as a draggable box
// with visible edges for its links. Read-only edges — link editing stays in the
// detail panel — but clicking a node selects it there, and drag positions are
// captured to a sibling <id>.layout.json so the layout persists without
// bloating the gameplay data.
//
// Kept self-contained and defensive: any GraphEdit hiccup is swallowed so a
// canvas problem can never break the (reliable) list editor it sits beside.
public partial class DialogueGraphCanvas : GraphEdit
{
	// Raised when a node box is clicked, so the panel shows it in the detail
	// editor. Distinct from GraphEdit's own NodeSelected signal, which this
	// class listens to internally.
	public Action<string> NodeClicked { get; set; }

	// GraphNode.Name (a safe generated id) <-> dialogue node id.
	private readonly Dictionary<string, string> nameToNode = new();
	private readonly Dictionary<string, string> nodeToName = new();
	private bool populated;

	private static readonly Color InPortColor = new(0.6f, 0.62f, 0.66f);
	private static readonly Color ChoicePortColor = new(0.6f, 0.75f, 0.95f);
	private static readonly Color NextPortColor = new(0.7f, 0.8f, 1f);
	private static readonly Color BranchPortColor = new(0.5f, 1f, 0.6f);

	public override void _Ready()
	{
		RightDisconnects = false;
		ShowGrid = true;
		// GraphEdit's own selection signal (distinct from our NodeClicked).
		NodeSelected += OnGraphNodeSelected;
	}

	// True once a graph has been drawn, so the panel only bothers capturing a
	// layout the author has actually seen/moved.
	public bool HasContent => populated;

	public void Populate(DialogueGraph graph, DialogueLayout layout)
	{
		try
		{
			Rebuild(graph, layout);
			populated = true;
		}
		catch (Exception e)
		{
			GD.PushWarning($"Dialogue graph canvas could not draw '{graph?.Id}': {e.Message}");
		}
	}

	// Reads the current box positions into a layout to save. Returns null when
	// nothing has been drawn.
	public DialogueLayout CaptureLayout(string graphId)
	{
		if (!populated)
		{
			return null;
		}
		var layout = new DialogueLayout { Id = graphId };
		foreach (var child in GetChildren())
		{
			if (child is GraphNode node && nameToNode.TryGetValue(node.Name, out var nodeId))
			{
				var offset = node.PositionOffset;
				layout.Set(nodeId, offset.X, offset.Y);
			}
		}
		return layout;
	}

	private void Rebuild(DialogueGraph graph, DialogueLayout layout)
	{
		ClearConnections();
		foreach (var child in GetChildren().OfType<GraphNode>())
		{
			child.QueueFree();
			RemoveChild(child);
		}
		nameToNode.Clear();
		nodeToName.Clear();
		if (graph == null)
		{
			return;
		}

		var depths = ComputeDepths(graph);
		var rowInDepth = new Dictionary<int, int>();
		var index = 0;
		foreach (var node in graph.Nodes)
		{
			var name = $"gn{index++}";
			nameToNode[name] = node.Id;
			nodeToName[node.Id] = name;
			var box = BuildBox(graph, node, name);
			AddChild(box);

			var saved = layout?.Get(node.Id);
			if (saved != null)
			{
				box.PositionOffset = new Vector2(saved.X, saved.Y);
			}
			else
			{
				var depth = depths.GetValueOrDefault(node.Id, 0);
				var row = rowInDepth.GetValueOrDefault(depth, 0);
				rowInDepth[depth] = row + 1;
				box.PositionOffset = new Vector2(depth * 320, row * 150);
			}
		}

		// Connect edges once every box exists (ConnectNode needs both names).
		foreach (var node in graph.Nodes)
		{
			var fromName = nodeToName[node.Id];
			var port = 0;
			foreach (var target in Outgoing(node).Select(o => o.target))
			{
				if (!string.IsNullOrEmpty(target) && nodeToName.TryGetValue(target, out var toName))
				{
					ConnectNode(fromName, port, toName, 0);
				}
				port++;
			}
		}
	}

	private GraphNode BuildBox(DialogueGraph graph, DialogueNode node, string name)
	{
		var isRouter = node.Branches != null;
		var box = new GraphNode
		{
			Name = name,
			Title = node.Id + (node.Id == graph.EntryNodeId ? "  ▶" : "") + (isRouter ? "  (router)" : ""),
		};

		// Row 0: input port (left), no output.
		var header = new Label { Text = isRouter ? "route" : Summarize(node) };
		box.AddChild(header);
		box.SetSlot(0, true, 0, InPortColor, false, 0, InPortColor);

		var outgoing = Outgoing(node).ToList();
		for (var i = 0; i < outgoing.Count; i++)
		{
			var row = new Label { Text = outgoing[i].label, HorizontalAlignment = HorizontalAlignment.Right };
			box.AddChild(row);
			box.SetSlot(i + 1, false, 0, InPortColor, true, 0, outgoing[i].color);
		}
		return box;
	}

	private static string Summarize(DialogueNode node)
	{
		var text = node.Text ?? "";
		return text.Length <= 40 ? text : text[..40] + "…";
	}

	// The node's outgoing links, in port order, each with a label, color, and
	// target node id.
	private static IEnumerable<(string label, Color color, string target)> Outgoing(DialogueNode node)
	{
		if (node.Branches != null)
		{
			foreach (var branch in node.Branches)
			{
				var label = branch.When == null ? "always" : "when " + branch.When.ToToken();
				yield return (label, BranchPortColor, branch.ToNodeId);
			}
			if (!string.IsNullOrEmpty(node.NextNodeId))
			{
				yield return ("otherwise", NextPortColor, node.NextNodeId);
			}
			yield break;
		}
		if (node.Choices is { Count: > 0 })
		{
			foreach (var choice in node.Choices)
			{
				yield return (choice.Label ?? "(choice)", ChoicePortColor, choice.NextNodeId);
			}
			yield break;
		}
		if (!string.IsNullOrEmpty(node.NextNodeId))
		{
			yield return ("Next", NextPortColor, node.NextNodeId);
		}
	}

	// Distance from the entry along all links, for a left-to-right auto-layout
	// when there is no saved position; unreached nodes fall to depth 0.
	private static Dictionary<string, int> ComputeDepths(DialogueGraph graph)
	{
		var depths = new Dictionary<string, int>();
		if (string.IsNullOrEmpty(graph.EntryNodeId) || graph.GetNode(graph.EntryNodeId) == null)
		{
			return depths;
		}
		var queue = new Queue<string>();
		queue.Enqueue(graph.EntryNodeId);
		depths[graph.EntryNodeId] = 0;
		while (queue.Count > 0)
		{
			var id = queue.Dequeue();
			var node = graph.GetNode(id);
			if (node == null)
			{
				continue;
			}
			foreach (var (_, _, target) in Outgoing(node))
			{
				if (string.IsNullOrEmpty(target) || depths.ContainsKey(target) || graph.GetNode(target) == null)
				{
					continue;
				}
				depths[target] = depths[id] + 1;
				queue.Enqueue(target);
			}
		}
		return depths;
	}

	private void OnGraphNodeSelected(Node node)
	{
		if (node is GraphNode graphNode && nameToNode.TryGetValue(graphNode.Name, out var nodeId))
		{
			NodeClicked?.Invoke(nodeId);
		}
	}
}
