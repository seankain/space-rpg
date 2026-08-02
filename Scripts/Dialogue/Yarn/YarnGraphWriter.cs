using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// Writes a DialogueGraph back out as Yarn source — the other half of the
// bridge (npc-dialogue-yarn.md Phase 1, dialogue-editor.md Phase 5's "Yarn
// convergence"). Two jobs:
//
//   * the in-game dialogue editor saves a conversation in the format it was
//     loaded from, so editing a .yarn conversation writes .yarn back instead of
//     spawning a rival .json with the same id (DialogueEditing.Save), and
//   * it converts an existing .dialogue.json conversation to Yarn.
//
// The output is deliberately literal: one Yarn node per graph node, links
// written as explicit <<jump>>s. That keeps ids stable and makes the write ->
// parse round-trip exact (Tests/YarnDialogueTests.cs), at the cost of being
// flatter than hand-authored Yarn, which nests an option's reply under it.
// Both forms compile to the same graph, so an author is free to tidy up.
public static class YarnGraphWriter
{
    public static string Write(DialogueGraph graph) => Write(graph, null);

    // problems collects nodes that can't be expressed in Yarn (today: a
    // displayed line with neither speaker nor text — nothing to write). The rest
    // of the file is still written, so a save reports the gap instead of
    // failing outright.
    public static string Write(DialogueGraph graph, List<string> problems)
    {
        if (graph == null)
        {
            return "";
        }
        var builder = new StringBuilder();
        builder.Append("// ").Append(graph.Id).Append(" — dialogue for the space-rpg dialogue runtime.\n");
        builder.Append("// Nodes are linked with <<jump>>; ").Append(DialogueGraph.SpeakerToken)
            .Append(" speaks as the NPC playing this conversation.\n");

        var order = NodeOrder(graph);
        foreach (var node in order)
        {
            builder.Append('\n');
            WriteNode(builder, graph, node, problems);
        }
        return builder.ToString();
    }

    // The entry node first, so a file without an `entry:` header still reads
    // top-down; the rest keep the graph's order.
    private static List<DialogueNode> NodeOrder(DialogueGraph graph)
    {
        var order = new List<DialogueNode>();
        var entry = graph.GetNode(graph.EntryNodeId);
        if (entry != null)
        {
            order.Add(entry);
        }
        foreach (var node in graph.Nodes)
        {
            if (node != null && node != entry)
            {
                order.Add(node);
            }
        }
        return order;
    }

    private static void WriteNode(
        StringBuilder builder, DialogueGraph graph, DialogueNode node, List<string> problems)
    {
        builder.Append(YarnParser.TitleHeader).Append(": ").Append(node.Id).Append('\n');
        if (node.Id == graph.EntryNodeId && graph.Nodes.Count > 0 && graph.Nodes[0] != node)
        {
            // Order alone would make the wrong node the entry on re-import.
            builder.Append(YarnParser.EntryHeader).Append(": true\n");
        }
        builder.Append(YarnParser.HeaderSeparator).Append('\n');

        if (node.Branches is { Count: > 0 })
        {
            WriteRouter(builder, node);
        }
        else
        {
            WriteLine(builder, node, problems);
        }
        builder.Append(YarnParser.NodeTerminator).Append('\n');
    }

    // A router is an <<if>>/<<elseif>> chain of jumps. Its unconditional last
    // branch is <<else>>; a NextNodeId fallback is the jump after <<endif>>,
    // which is exactly the fall-through the compiler reads back.
    private static void WriteRouter(StringBuilder builder, DialogueNode node)
    {
        var opened = false;
        foreach (var branch in node.Branches)
        {
            var condition = ConditionText(branch.When);
            if (condition == null)
            {
                // Unconditional: every branch below it is unreachable, so this
                // one closes the chain.
                if (!opened)
                {
                    // A router whose first branch is unconditional only
                    // redirects; a node that is nothing but a <<jump>> says that.
                    AppendJump(builder, branch.ToNodeId, 0);
                    return;
                }
                builder.Append("<<else>>\n");
                AppendJump(builder, branch.ToNodeId, 4);
                builder.Append("<<endif>>\n");
                return;
            }
            builder.Append(opened ? "<<elseif " : "<<if ").Append(condition).Append(">>\n");
            AppendJump(builder, branch.ToNodeId, 4);
            opened = true;
        }
        if (opened)
        {
            builder.Append("<<endif>>\n");
        }
        if (!string.IsNullOrEmpty(node.NextNodeId))
        {
            AppendJump(builder, node.NextNodeId, 0);
        }
    }

    private static void WriteLine(StringBuilder builder, DialogueNode node, List<string> problems)
    {
        foreach (var effect in node.OnShownEffects ?? new List<EffectRef>())
        {
            AppendCommand(builder, effect, 0);
        }

        var speaker = node.Speaker;
        var text = node.Text ?? "";
        if (string.IsNullOrEmpty(speaker) && text.Length == 0)
        {
            problems?.Add(
                $"node '{node.Id}' has no speaker and no text, so there is no Yarn line to write for it.");
        }
        else if (string.IsNullOrEmpty(speaker))
        {
            builder.Append(EscapeText(text)).Append('\n');
        }
        else
        {
            builder.Append(speaker).Append(": ").Append(EscapeText(text)).Append('\n');
        }

        if (node.Choices is { Count: > 0 })
        {
            foreach (var choice in node.Choices)
            {
                WriteOption(builder, choice);
            }
            if (!string.IsNullOrEmpty(node.NextNodeId))
            {
                // The graph falls through to Next when every choice is gated
                // away; Yarn has no way to say "the options were all hidden".
                problems?.Add(
                    $"node '{node.Id}' has both choices and a Next link; Yarn can't express the "
                    + "fall-through, so the Next link was dropped.");
            }
            return;
        }
        if (!string.IsNullOrEmpty(node.NextNodeId))
        {
            AppendJump(builder, node.NextNodeId, 0);
        }
    }

    private static void WriteOption(StringBuilder builder, DialogueChoiceData choice)
    {
        builder.Append("-> ").Append(EscapeText(choice.Label ?? ""));
        var condition = ConditionText(choice.Visible);
        if (condition != null)
        {
            builder.Append(" <<if ").Append(condition).Append(">>");
        }
        builder.Append('\n');
        foreach (var effect in choice.Effects ?? new List<EffectRef>())
        {
            AppendCommand(builder, effect, 4);
        }
        if (string.IsNullOrEmpty(choice.NextNodeId))
        {
            // Explicit, so the option's body is never empty and the intent
            // ("this ends the conversation") is on the page.
            Indent(builder, 4).Append("<<stop>>\n");
            return;
        }
        AppendJump(builder, choice.NextNodeId, 4);
    }

    private static void AppendJump(StringBuilder builder, string target, int indent)
    {
        if (string.IsNullOrEmpty(target))
        {
            Indent(builder, indent).Append("<<stop>>\n");
            return;
        }
        Indent(builder, indent).Append("<<jump ").Append(target).Append(">>\n");
    }

    private static void AppendCommand(StringBuilder builder, EffectRef effect, int indent)
    {
        if (effect == null || string.IsNullOrEmpty(effect.Id))
        {
            return;
        }
        Indent(builder, indent).Append("<<").Append(effect.Id);
        foreach (var arg in effect.Args ?? Array.Empty<string>())
        {
            builder.Append(' ').Append(QuoteIfNeeded(arg));
        }
        builder.Append(">>\n");
    }

    // The inverse of YarnGraphCompiler.ParseCondition: a call, with string
    // arguments quoted so the file also reads as valid Yarn. A query verb writes
    // its comparison out in full (`credits() >= 200`) and a negated one takes a
    // `not` prefix, so the round trip through the token is exact. Null for "no
    // condition", which the caller renders as <<else>> or an ungated option.
    public static string ConditionText(ConditionRef condition)
    {
        if (condition == null || string.IsNullOrEmpty(condition.Id))
        {
            return null;
        }
        var id = DialogueConditions.BareId(condition.Id);
        var args = condition.Args ?? Array.Empty<string>();
        string op = null;
        string value = null;
        if (DialogueQueries.IsQuery(id)
            && DialogueConditions.TrySplitComparison(id, args, out var callArgs, out var found, out var right))
        {
            args = callArgs;
            op = found;
            value = right;
        }

        // A negated comparison writes as the inverted operator, not as
        // `not x >= y`, which Yarn would read as `(not x) >= y`. The compiler
        // folds these on the way in, so only a hand-built token gets here.
        var negated = DialogueConditions.IsNegated(condition.Id);
        if (negated && op != null && DialogueConditions.InvertOperator(op) is { } inverted)
        {
            op = inverted;
            negated = false;
        }

        var builder = new StringBuilder();
        if (negated)
        {
            builder.Append("not ");
        }
        builder.Append(id).Append('(');
        for (var i = 0; i < args.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }
            builder.Append(IsNumber(args[i]) ? args[i] : Quote(args[i]));
        }
        builder.Append(')');
        if (op != null)
        {
            builder.Append(' ').Append(op).Append(' ')
                .Append(IsNumber(value) ? value : Quote(value));
        }
        return builder.ToString();
    }

    private static StringBuilder Indent(StringBuilder builder, int indent) =>
        builder.Append(' ', indent);

    private static bool IsNumber(string value) =>
        !string.IsNullOrEmpty(value)
        && long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);

    private static string QuoteIfNeeded(string arg)
    {
        if (string.IsNullOrEmpty(arg))
        {
            return "\"\"";
        }
        return arg.IndexOf(' ') >= 0 || arg.IndexOf('"') >= 0 ? Quote(arg) : arg;
    }

    private static string Quote(string value) =>
        "\"" + (value ?? "").Replace("\"", "") + "\"";

    // A colon early in a line would read back as a speaker name, and a leading
    // "->" as an option, so both are escaped. Line tags (#) and comments (//)
    // get the same treatment for the same reason.
    private static string EscapeText(string text)
    {
        // Backslashes first, so an authored one survives the escapes added below.
        var escaped = (text ?? "").Replace("\\", "\\\\");
        var colon = escaped.IndexOf(':');
        if (colon >= 0 && colon <= YarnParser.MaxSpeakerLength)
        {
            escaped = escaped[..colon] + "\\:" + escaped[(colon + 1)..];
        }
        escaped = escaped.Replace("#", "\\#").Replace("//", "\\/\\/");
        return escaped.StartsWith("->", StringComparison.Ordinal) ? "\\" + escaped : escaped;
    }
}
