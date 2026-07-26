using System.Collections.Generic;
using System.Linq;

// Pre-save validation for the dialogue editor (dialogue-editor plan Phase 4),
// engine-free so it is unit-tested and the panel can run it inline on every
// edit. Errors block a save (a dangling link, a missing entry, a bad
// effect/condition arg); warnings do not (an orphan node unreachable from the
// entry). The vocabulary checks defer to DialogueEffects/DialogueConditions so
// there is one source of truth for what parses.
public enum DialogueSeverity
{
    Error,
    Warning,
}

public sealed class DialogueIssue
{
    public DialogueSeverity Severity { get; }
    public string Message { get; }

    public DialogueIssue(DialogueSeverity severity, string message)
    {
        Severity = severity;
        Message = message;
    }

    public override string ToString() => $"{Severity}: {Message}";
}

public static class DialogueValidation
{
    public static List<DialogueIssue> Validate(DialogueGraph graph)
    {
        var issues = new List<DialogueIssue>();
        if (graph == null)
        {
            issues.Add(new DialogueIssue(DialogueSeverity.Error, "graph is null"));
            return issues;
        }

        void Error(string m) => issues.Add(new DialogueIssue(DialogueSeverity.Error, m));
        void Warn(string m) => issues.Add(new DialogueIssue(DialogueSeverity.Warning, m));

        if (string.IsNullOrEmpty(graph.Id))
        {
            Error("dialogue has no id");
        }

        var ids = new HashSet<string>();
        foreach (var node in graph.Nodes)
        {
            if (string.IsNullOrEmpty(node.Id))
            {
                Error("a node has no id");
            }
            else if (!ids.Add(node.Id))
            {
                Error($"duplicate node id '{node.Id}'");
            }
        }

        if (string.IsNullOrEmpty(graph.EntryNodeId))
        {
            Error("no entry node is set");
        }
        else if (!ids.Contains(graph.EntryNodeId))
        {
            Error($"entry node '{graph.EntryNodeId}' does not exist");
        }

        // A non-empty link to a missing node is an error; an empty link is a
        // deliberate end-of-conversation.
        void CheckLink(string target, string where)
        {
            if (!string.IsNullOrEmpty(target) && !ids.Contains(target))
            {
                Error($"{where} links to unknown node '{target}'");
            }
        }

        foreach (var node in graph.Nodes)
        {
            var at = $"node '{node.Id}'";
            if (node.Branches is { Count: > 0 })
            {
                CheckLink(node.NextNodeId, $"{at} fallback");
                foreach (var branch in node.Branches)
                {
                    if (DialogueConditions.Validate(branch.When) is { } conditionError)
                    {
                        Error($"{at} branch: {conditionError}");
                    }
                    CheckLink(branch.ToNodeId, $"{at} branch");
                }
                continue;
            }
            CheckLink(node.NextNodeId, $"{at} Next");
            foreach (var effect in node.OnShownEffects ?? Enumerable.Empty<EffectRef>())
            {
                if (DialogueEffects.Validate(effect) is { } effectError)
                {
                    Error($"{at} on-shown: {effectError}");
                }
            }
            foreach (var choice in node.Choices ?? Enumerable.Empty<DialogueChoiceData>())
            {
                var atChoice = $"{at} choice '{choice.Label}'";
                CheckLink(choice.NextNodeId, atChoice);
                if (DialogueConditions.Validate(choice.Visible) is { } visibleError)
                {
                    Error($"{atChoice}: {visibleError}");
                }
                foreach (var effect in choice.Effects ?? Enumerable.Empty<EffectRef>())
                {
                    if (DialogueEffects.Validate(effect) is { } choiceEffectError)
                    {
                        Error($"{atChoice}: {choiceEffectError}");
                    }
                }
            }
        }

        foreach (var orphan in Unreachable(graph, ids))
        {
            Warn($"node '{orphan}' is unreachable from the entry node");
        }

        return issues;
    }

    public static bool HasErrors(IEnumerable<DialogueIssue> issues) =>
        issues.Any(i => i.Severity == DialogueSeverity.Error);

    // Node ids not reachable from the entry by following Next, choice targets,
    // and router branch/fallback targets (conditions ignored — a node counts as
    // reachable if any path can land on it).
    private static IEnumerable<string> Unreachable(DialogueGraph graph, HashSet<string> ids)
    {
        if (string.IsNullOrEmpty(graph.EntryNodeId) || !ids.Contains(graph.EntryNodeId))
        {
            return Enumerable.Empty<string>();
        }
        var reached = new HashSet<string>();
        var stack = new Stack<string>();
        stack.Push(graph.EntryNodeId);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!reached.Add(id))
            {
                continue;
            }
            var node = graph.GetNode(id);
            if (node == null)
            {
                continue;
            }
            foreach (var next in Successors(node))
            {
                if (!string.IsNullOrEmpty(next) && ids.Contains(next))
                {
                    stack.Push(next);
                }
            }
        }
        return ids.Where(id => !reached.Contains(id));
    }

    private static IEnumerable<string> Successors(DialogueNode node)
    {
        if (node.Branches is { Count: > 0 })
        {
            foreach (var branch in node.Branches)
            {
                yield return branch.ToNodeId;
            }
            yield return node.NextNodeId;
            yield break;
        }
        yield return node.NextNodeId;
        foreach (var choice in node.Choices ?? Enumerable.Empty<DialogueChoiceData>())
        {
            yield return choice.NextNodeId;
        }
    }
}
