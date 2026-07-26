using System.Collections.Generic;

// Compiles a serialized DialogueGraph into the existing DialogueLine/
// DialogueChoice runtime tree DialogueManager already plays (dialogue-editor
// plan Phase 1). This is the whole point of the split: authoring and editing
// happen on the flat, id-linked graph; the manager stays untouched and keeps
// walking a linked tree.
//
// Compilation resolves node ids to Next links, redirects through router nodes,
// substitutes the speaking NPC's name for DialogueGraph.SpeakerToken, filters
// each node's choices by their visibility condition, and wires OnShown/Action
// to DialogueEffects.Run. Conditions (router branches and choice gates) are
// evaluated once, at conversation start, matching how the role code decided
// branches at BuildDialogue time. A dangling link or a routing cycle is
// reported through the context and treated as end-of-conversation, never a
// crash.
public static class DialogueRuntime
{
    public static DialogueLine Compile(DialogueGraph graph, DialogueContext context) =>
        Compile(graph, context, null);

    // Overload that starts compilation at an arbitrary node — the editor's
    // "play from here" preview (dialogue-editor plan Phase 5) runs the open
    // graph from the selected node. A null/empty startNodeId uses the entry.
    public static DialogueLine Compile(DialogueGraph graph, DialogueContext context, string startNodeId)
    {
        if (graph == null)
        {
            return null;
        }
        var start = string.IsNullOrEmpty(startNodeId) ? graph.EntryNodeId : startNodeId;
        return new Compiler(graph, context).Build(start);
    }

    private sealed class Compiler
    {
        private readonly DialogueGraph graph;
        private readonly DialogueContext context;
        // Memoize by node id: a node reached from several places (or a graph
        // with a back-edge) reuses the one DialogueLine, which also stops
        // cycles from recursing forever. A router resolving to end-of-
        // conversation is cached as null.
        private readonly Dictionary<string, DialogueLine> built = new();
        // Router ids currently being resolved, to break a router->router cycle.
        private readonly HashSet<string> routing = new();

        public Compiler(DialogueGraph graph, DialogueContext context)
        {
            this.graph = graph;
            this.context = context;
        }

        public DialogueLine Build(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId))
            {
                return null;
            }
            if (built.TryGetValue(nodeId, out var existing))
            {
                return existing;
            }
            var node = graph.GetNode(nodeId);
            if (node == null)
            {
                context?.Warn($"Dialogue '{graph.Id}' links to unknown node id '{nodeId}'; ending the conversation there.");
                return null;
            }
            return node.Branches is { Count: > 0 } ? BuildRouter(nodeId, node) : BuildLine(nodeId, node);
        }

        // A router displays nothing: pick the first branch whose condition
        // holds (a null condition always matches, so it is the fallback), then
        // resolve straight to that target's line.
        private DialogueLine BuildRouter(string nodeId, DialogueNode node)
        {
            if (!routing.Add(nodeId))
            {
                context?.Warn($"Dialogue '{graph.Id}' has a routing cycle at node '{nodeId}'; ending there.");
                return null;
            }
            var target = node.NextNodeId;
            foreach (var branch in node.Branches)
            {
                if (DialogueConditions.Evaluate(branch.When, context))
                {
                    target = branch.ToNodeId;
                    break;
                }
            }
            var resolved = Build(target);
            routing.Remove(nodeId);
            built[nodeId] = resolved;
            return resolved;
        }

        private DialogueLine BuildLine(string nodeId, DialogueNode node)
        {
            var line = new DialogueLine
            {
                Speaker = ResolveSpeaker(node.Speaker),
                Text = node.Text,
            };
            // Register before recursing so a self- or back-reference resolves
            // to this instance instead of looping.
            built[nodeId] = line;

            if (node.OnShownEffects is { Count: > 0 } onShown)
            {
                line.OnShown = () => RunAll(onShown);
            }

            var choices = BuildChoices(node);
            if (choices != null && choices.Count > 0)
            {
                line.Choices = choices;
            }
            else
            {
                // Only a choice-less line follows NextNodeId, matching how
                // DialogueManager ignores Next when choices are present.
                line.Next = Build(node.NextNodeId);
            }
            return line;
        }

        private List<DialogueChoice> BuildChoices(DialogueNode node)
        {
            if (node.Choices == null || node.Choices.Count == 0)
            {
                return null;
            }
            var result = new List<DialogueChoice>();
            foreach (var choice in node.Choices)
            {
                if (!DialogueConditions.Evaluate(choice.Visible, context))
                {
                    continue;
                }
                var effects = choice.Effects;
                result.Add(new DialogueChoice
                {
                    Label = choice.Label,
                    Action = effects is { Count: > 0 } ? () => RunAll(effects) : null,
                    Next = Build(choice.NextNodeId),
                });
            }
            return result;
        }

        private void RunAll(List<EffectRef> effects)
        {
            foreach (var effect in effects)
            {
                DialogueEffects.Run(effect, context);
            }
        }

        private string ResolveSpeaker(string speaker) =>
            speaker == DialogueGraph.SpeakerToken ? context?.SpeakerName : speaker;
    }
}
