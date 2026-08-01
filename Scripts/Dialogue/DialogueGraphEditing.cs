using System.Collections.Generic;

// Graph mutations the editor performs, kept engine-free so they are unit-tested
// and the panel stays a thin view over them (dialogue-editor plan Phase 4). The
// non-trivial ones (delete, rename) keep the graph's links consistent — the
// point of storing node ids is that the editor can rewire them safely.
public static class DialogueGraphEditing
{
    // A working copy the editor mutates without touching the cached catalog
    // graph until save (round-trips through JSON, so it is a deep clone). The
    // source file and format ride along by hand — they are deliberately not
    // serialized, but a save has to know which file it is rewriting.
    public static DialogueGraph Clone(DialogueGraph graph)
    {
        if (graph == null)
        {
            return null;
        }
        var clone = DialogueSerialization.FromJson(DialogueSerialization.ToJson(graph));
        clone.SourceFormat = graph.SourceFormat;
        clone.SourcePath = graph.SourcePath;
        return clone;
    }

    // A fresh conversation: one empty line node that is also the entry. It is
    // Yarn from the start, because that is what a save writes.
    public static DialogueGraph NewEmpty(string id) => new()
    {
        Id = id,
        SourceFormat = DialogueSourceFormat.Yarn,
        EntryNodeId = "start",
        Nodes = new List<DialogueNode>
        {
            new() { Id = "start", Speaker = DialogueGraph.SpeakerToken, Text = "" },
        },
    };

    // Appends a new node (a plain line, or a router) with a generated unique id
    // and returns it.
    public static DialogueNode AddNode(DialogueGraph graph, bool router)
    {
        var node = new DialogueNode { Id = UniqueNodeId(graph, router ? "router" : "node") };
        if (router)
        {
            node.Branches = new List<DialogueBranch>();
        }
        else
        {
            node.Speaker = DialogueGraph.SpeakerToken;
            node.Text = "";
        }
        graph.Nodes.Add(node);
        return node;
    }

    // Removes a node and clears every link that pointed at it (those become
    // end-of-conversation), so a delete never leaves a dangling target.
    public static bool DeleteNode(DialogueGraph graph, string id)
    {
        var node = graph.GetNode(id);
        if (node == null)
        {
            return false;
        }
        graph.Nodes.Remove(node);
        foreach (var other in graph.Nodes)
        {
            if (other.NextNodeId == id)
            {
                other.NextNodeId = null;
            }
            foreach (var choice in other.Choices ?? Empty<DialogueChoiceData>())
            {
                if (choice.NextNodeId == id)
                {
                    choice.NextNodeId = null;
                }
            }
            foreach (var branch in other.Branches ?? Empty<DialogueBranch>())
            {
                if (branch.ToNodeId == id)
                {
                    branch.ToNodeId = null;
                }
            }
        }
        return true;
    }

    // Renames a node and repoints the entry and every link that referenced it.
    // Fails (returns false) on an empty new id or a collision with an existing
    // node.
    public static bool RenameNode(DialogueGraph graph, string oldId, string newId)
    {
        if (string.IsNullOrEmpty(newId))
        {
            return false;
        }
        if (oldId == newId)
        {
            return true;
        }
        if (graph.GetNode(newId) != null)
        {
            return false;
        }
        var node = graph.GetNode(oldId);
        if (node == null)
        {
            return false;
        }
        node.Id = newId;
        if (graph.EntryNodeId == oldId)
        {
            graph.EntryNodeId = newId;
        }
        foreach (var other in graph.Nodes)
        {
            if (other.NextNodeId == oldId)
            {
                other.NextNodeId = newId;
            }
            foreach (var choice in other.Choices ?? Empty<DialogueChoiceData>())
            {
                if (choice.NextNodeId == oldId)
                {
                    choice.NextNodeId = newId;
                }
            }
            foreach (var branch in other.Branches ?? Empty<DialogueBranch>())
            {
                if (branch.ToNodeId == oldId)
                {
                    branch.ToNodeId = newId;
                }
            }
        }
        return true;
    }

    // Reorders an item within a list by a delta, clamped — the move up/down
    // buttons for choices and router branches.
    public static void Move<T>(List<T> list, int index, int delta)
    {
        if (list == null || index < 0 || index >= list.Count)
        {
            return;
        }
        var target = index + delta;
        if (target < 0 || target >= list.Count)
        {
            return;
        }
        (list[index], list[target]) = (list[target], list[index]);
    }

    private static string UniqueNodeId(DialogueGraph graph, string prefix)
    {
        for (var i = 1; ; i++)
        {
            var candidate = $"{prefix}_{i}";
            if (graph.GetNode(candidate) == null)
            {
                return candidate;
            }
        }
    }

    private static IEnumerable<T> Empty<T>() => System.Array.Empty<T>();
}
