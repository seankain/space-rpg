using System.Collections.Generic;

// Compiles a serialized DialogueGraph into the existing DialogueLine/
// DialogueChoice runtime tree DialogueManager already plays (dialogue-editor
// plan Phase 1). This is the whole point of the split: authoring and editing
// happen on the flat, id-linked graph; the manager stays untouched and keeps
// walking a linked tree.
//
// Compilation resolves node ids to Next links, substitutes the speaking NPC's
// name for DialogueGraph.SpeakerToken, filters each node's choices by their
// visibility condition (evaluated once against the play context, matching how
// the role code decided branches at BuildDialogue time), and wires OnShown/
// Action to DialogueEffects.Run. A dangling link is reported through the
// context and treated as end-of-conversation, never a crash.
public static class DialogueRuntime
{
    public static DialogueLine Compile(DialogueGraph graph, DialogueContext context)
    {
        if (graph == null)
        {
            return null;
        }
        // Memoize by node id: a node reached from several places (or a graph
        // with a back-edge) reuses the one DialogueLine, which also stops
        // cycles from recursing forever.
        var built = new Dictionary<string, DialogueLine>();
        return Build(graph.EntryNodeId, graph, context, built);
    }

    private static DialogueLine Build(
        string nodeId,
        DialogueGraph graph,
        DialogueContext context,
        Dictionary<string, DialogueLine> built)
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

        var line = new DialogueLine
        {
            Speaker = ResolveSpeaker(node.Speaker, context),
            Text = node.Text,
        };
        // Register before recursing so a self- or back-reference resolves to
        // this instance instead of looping.
        built[nodeId] = line;

        if (node.OnShownEffect != null && !string.IsNullOrEmpty(node.OnShownEffect.Id))
        {
            var effect = node.OnShownEffect;
            line.OnShown = () => DialogueEffects.Run(effect, context);
        }

        var choices = BuildChoices(node, graph, context, built);
        if (choices != null && choices.Count > 0)
        {
            line.Choices = choices;
        }
        else
        {
            // Only a choice-less line follows NextNodeId, matching how
            // DialogueManager ignores Next when choices are present.
            line.Next = Build(node.NextNodeId, graph, context, built);
        }
        return line;
    }

    private static List<DialogueChoice> BuildChoices(
        DialogueNode node,
        DialogueGraph graph,
        DialogueContext context,
        Dictionary<string, DialogueLine> built)
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
            var effect = choice.Effect;
            result.Add(new DialogueChoice
            {
                Label = choice.Label,
                Action = effect != null && !string.IsNullOrEmpty(effect.Id)
                    ? () => DialogueEffects.Run(effect, context)
                    : null,
                Next = Build(choice.NextNodeId, graph, context, built),
            });
        }
        return result;
    }

    private static string ResolveSpeaker(string speaker, DialogueContext context) =>
        speaker == DialogueGraph.SpeakerToken ? context?.SpeakerName : speaker;
}
