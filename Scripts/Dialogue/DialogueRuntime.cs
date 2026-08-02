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
// to DialogueEffects.Run. A dangling link or a routing cycle is reported
// through the context and treated as end-of-conversation, never a crash.
//
// **Compilation is lazy** (dialogue-state-conditions plan Phase 2): only the
// first line is built up front, and each line's choices and following line are
// worked out when the box asks for them. Conditions are therefore evaluated
// when the node is *reached*, not when the conversation starts, which is what
// lets a conversation branch on state it just changed — spend credits on a
// choice, and the node it leads to sees the new balance. Nothing recurses ahead
// of the player, so a graph with a back-edge simply plays around again; only
// routers still resolve on arrival (they display nothing), and a router->router
// cycle is caught by the routing guard.
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
        // Router ids currently being resolved, to break a router->router cycle.
        // Routers are the one thing still resolved eagerly — arriving at one
        // means passing straight through it — so they are the only place a
        // build can still recurse.
        private readonly HashSet<string> routing = new();

        public Compiler(DialogueGraph graph, DialogueContext context)
        {
            this.graph = graph;
            this.context = context;
        }

        // The Compiler outlives the Compile call: every resolver a line carries
        // closes over it, so it is what walks the rest of the conversation as
        // the player does.
        public DialogueLine Build(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId))
            {
                return null;
            }
            var node = graph.GetNode(nodeId);
            if (node == null)
            {
                context?.Warn($"Dialogue '{graph.Id}' links to unknown node id '{nodeId}'; ending the conversation there.");
                return null;
            }
            return node.Branches is { Count: > 0 } ? BuildRouter(nodeId, node) : BuildLine(node);
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
            return resolved;
        }

        private DialogueLine BuildLine(DialogueNode node)
        {
            var line = new DialogueLine
            {
                Speaker = ResolveSpeaker(node.Speaker),
                Text = node.Text,
            };

            if (node.OnShownEffects is { Count: > 0 } onShown)
            {
                line.OnShown = () => RunAll(onShown);
            }

            line.ResolveChoices(() => BuildChoices(node));
            // Choices win over Next, matching DialogueManager. Deciding that
            // means reading the choices, which resolves their gates — once,
            // since the answer is remembered on the line either way.
            line.ResolveNext(() => line.Choices is { Count: > 0 } ? null : Build(node.NextNodeId));
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
                var target = choice.NextNodeId;
                var built = new DialogueChoice
                {
                    Label = choice.Label,
                    Action = effects is { Count: > 0 } ? () => RunAll(effects) : null,
                };
                // Resolved after the choice is picked, so the gates on the far
                // side see whatever its effects just did.
                built.ResolveNext(() => Build(target));
                result.Add(built);
            }
            // Every choice gated out is the same as having none: the line falls
            // through to its Next link.
            return result.Count > 0 ? result : null;
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
