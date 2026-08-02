using System;
using System.Collections.Generic;
using System.Text;

// Compiles parsed Yarn source into a DialogueGraph (npc-dialogue-yarn.md
// Phase 1). This is the whole integration strategy: Yarn is the *authoring*
// format, the existing graph stays the runtime format, so DialogueRuntime,
// DialogueManager, the effect/condition vocabulary, the validator, and the
// in-game editor all keep working untouched — a .yarn file is just another way
// to write the same conversation. Engine-free, so the xunit build covers it.
//
// The mapping:
//
//   title: offer            ->  a node whose id is the title (the first graph
//   ---                         node the body emits; later ones get `offer__2`)
//   $npc: Care to help?     ->  Speaker/Text ($npc is DialogueGraph.SpeakerToken)
//   <<give_item 4 1>>       ->  an EffectRef, on the next line's OnShownEffects
//                               (or the previous line's, at the end of a block)
//   -> Sure <<if has_item(1)>>  ->  a choice with a Visible condition; commands
//       <<take_item 1>>          at the top of its body are the choice's Effects
//       <<jump thanks>>      ->  the choice's NextNodeId
//   <<if quest_state(1, "Success")>> ... <<else>> ... <<endif>>
//                           ->  a router node with one branch per clause
//   <<stop>>                ->  end of conversation (an empty link)
//   ===
//
// Anything the graph can't express is reported as a line-numbered problem
// rather than silently mistranslated. Problems don't abort the compile: a graph
// still comes back (so the catalog can load it and the editor's validator can
// show the same trouble inline), matching how the rest of the dialogue layer
// warns and keeps playing.
public static class YarnGraphCompiler
{
    public const string FileSuffix = ".yarn";

    // Values of the `entry:` header that mark a node as the conversation's
    // entry. Without one, the first node in the file is the entry.
    private static readonly string[] TrueHeaders = { "true", "yes", "1" };

    public static DialogueGraph Compile(string id, string source, List<string> problems)
    {
        problems ??= new List<string>();
        var script = YarnParser.Parse(source, problems);
        return Compile(id, script, problems);
    }

    public static DialogueGraph Compile(string id, YarnScript script, List<string> problems)
    {
        problems ??= new List<string>();
        var graph = new DialogueGraph { Id = id, SourceFormat = DialogueSourceFormat.Yarn };
        if (script == null || script.Nodes.Count == 0)
        {
            problems.Add("the file has no dialogue nodes.");
            return graph;
        }
        new Compiler(graph, problems).Run(script);
        return graph;
    }

    // The conversation id a .yarn file's contents belong to: its file stem,
    // matching the <id>.dialogue.json convention.
    public static string IdFromFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return null;
        }
        var slash = fileName.LastIndexOfAny(new[] { '/', '\\' });
        var name = slash < 0 ? fileName : fileName[(slash + 1)..];
        return name.EndsWith(FileSuffix, StringComparison.OrdinalIgnoreCase)
            ? name[..^FileSuffix.Length]
            : name;
    }

    // `has_item(1)` / `quest_state(1, "Success")` / `party_has_room()` — a call
    // to one of the DialogueConditions verbs, which is also how the condition
    // reads as real Yarn (a function call returning a bool). A query verb is
    // compared to a literal the same way Yarn compares a function's result
    // (`credits() >= 200`), and any condition may be negated with `not` or `!`
    // (dialogue-state-conditions plan Phase 1). What is still refused, by line
    // number rather than by guesswork: Yarn $variables, arithmetic, and
    // and/or/xor — a condition is one call, one optional comparison. Shared with
    // YarnGraphWriter's inverse.
    public static ConditionRef ParseCondition(string text, int line, List<string> problems)
    {
        var source = (text ?? "").Trim();
        if (source.Length == 0)
        {
            problems.Add($"line {line}: <<if>> has no condition.");
            return null;
        }
        if (source.Contains('$'))
        {
            problems.Add(
                $"line {line}: Yarn variables aren't supported — dialogue reads game state through "
                + $"conditions ({string.Join(", ", DialogueConditions.Ids)}).");
            return null;
        }

        // `not has_item(1)` / `!has_item(1)`: negation is a prefix, and the only
        // boolean operator there is. A second `not` inside falls through to the
        // combinator check below.
        var negated = false;
        if (source.StartsWith("not ", StringComparison.OrdinalIgnoreCase)
            || source.Equals("not", StringComparison.OrdinalIgnoreCase))
        {
            negated = true;
            source = source[3..].Trim();
        }
        else if (source.StartsWith("!", StringComparison.Ordinal)
            && !source.StartsWith("!=", StringComparison.Ordinal))
        {
            negated = true;
            source = source[1..].Trim();
        }
        if (source.Length == 0)
        {
            problems.Add($"line {line}: '{(text ?? "").Trim()}' negates nothing.");
            return null;
        }

        foreach (var combinator in new[] { "&&", "||" })
        {
            if (source.Contains(combinator, StringComparison.Ordinal))
            {
                problems.Add(
                    $"line {line}: '{combinator}' isn't supported — a condition is one call, optionally "
                    + "compared to a literal. Write 'and' as nested <<if>> blocks and 'or' as separate "
                    + "<<elseif>> clauses.");
                return null;
            }
        }
        foreach (var word in new[] { "not", "and", "or", "xor" })
        {
            if (ContainsWordOutsideQuotes(source, word))
            {
                problems.Add(
                    $"line {line}: '{word}' isn't supported — a condition is one call, optionally "
                    + "compared to a literal (credits() >= 200) and optionally negated (not has_item(1)). "
                    + "Write 'and' as nested <<if>> blocks and 'or' as separate <<elseif>> clauses.");
                return null;
            }
        }
        foreach (var word in new[] { "eq", "neq", "is", "gt", "lt", "gte", "lte" })
        {
            if (ContainsWordOutsideQuotes(source, word))
            {
                problems.Add(
                    $"line {line}: write Yarn's '{word}' operator symbolically "
                    + $"({string.Join(", ", DialogueConditions.Operators)}).");
                return null;
            }
        }
        var arithmetic = IndexOutsideQuotes(source, "+*/%");
        if (arithmetic >= 0)
        {
            problems.Add(
                $"line {line}: arithmetic ('{source[arithmetic]}') isn't supported in a condition — "
                + "compare a query to a literal instead.");
            return null;
        }

        // Split off a comparison, if there is one: everything left of the
        // operator is the call, everything right of it the literal.
        var left = source;
        string op = null;
        string right = null;
        var at = FindComparison(source, out var found);
        if (at >= 0)
        {
            op = found;
            left = source[..at].Trim();
            right = source[(at + found.Length)..].Trim();
            if (left.Length == 0 || right.Length == 0)
            {
                problems.Add($"line {line}: '{source}' is missing one side of its '{found}' comparison.");
                return null;
            }
        }

        // `not credits() >= 200` is stored as `credits() < 200`: same meaning,
        // and it survives being written back out as unambiguous Yarn (see
        // DialogueConditions.InvertOperator).
        if (negated && op != null)
        {
            op = DialogueConditions.InvertOperator(op);
            negated = false;
        }

        if (!TryParseCall(left, out var name, out var args, out var callProblem))
        {
            problems.Add($"line {line}: {callProblem}");
            return null;
        }

        if (op != null)
        {
            if (!TryLiteral(right, out var literal))
            {
                problems.Add(
                    $"line {line}: '{right}' isn't a literal — the right side of a comparison is a number "
                    + "or a quoted string.");
                return null;
            }
            args = Append(args, op, literal);
        }
        else if (DialogueQueries.IsQuery(name) && args.Length == DialogueQueries.Arity(name) + 1)
        {
            // A query written the pre-plan way — party_size(2), stat("Charisma",
            // 12) — is the elided spelling of the kind's default comparison.
            // Normalize it into the token so the writer only ever emits one form
            // and the round trip is exact.
            args = Append(args[..^1], DialogueConditions.DefaultOperator(name), args[^1]);
        }

        var condition = new ConditionRef
        {
            Id = negated ? DialogueConditions.Negation + name : name,
            Args = args,
        };
        if (Array.IndexOf(DialogueConditions.Ids, name) < 0)
        {
            problems.Add(
                $"line {line}: unknown condition '{name}'. Known: {string.Join(", ", DialogueConditions.Ids)}.");
        }
        else if (DialogueConditions.Validate(condition) is { } reason)
        {
            problems.Add($"line {line}: {reason}.");
        }
        return condition;
    }

    // `name(arg, "arg")` or a bare `name`, which is the same as `name()`.
    private static bool TryParseCall(string source, out string name, out string[] args, out string problem)
    {
        name = null;
        args = Array.Empty<string>();
        problem = null;
        var open = source.IndexOf('(');
        if (open < 0)
        {
            name = source;
        }
        else
        {
            if (!source.EndsWith(")", StringComparison.Ordinal))
            {
                problem = $"condition '{source}' is missing its closing ')'.";
                return false;
            }
            name = source[..open].Trim();
            args = SplitConditionArgs(source[(open + 1)..^1]);
        }
        if (name.Length == 0)
        {
            problem = $"condition '{source}' has no name.";
            return false;
        }
        return true;
    }

    // The right side of a comparison: a quoted string, or a bare token (a number,
    // or an unquoted word like a quest state's name).
    private static bool TryLiteral(string source, out string value)
    {
        value = null;
        if (source.Length >= 2 && source[0] == '"' && source[^1] == '"')
        {
            value = source[1..^1];
            return !value.Contains('"');
        }
        if (source.IndexOfAny(new[] { '(', ')', '"', ' ', '\t' }) >= 0)
        {
            return false;
        }
        value = source;
        return true;
    }

    // The first comparison operator at the top level — outside quotes, and
    // outside the call's own parentheses. -1 when there isn't one.
    private static int FindComparison(string source, out string op)
    {
        op = null;
        var depth = 0;
        var quoted = false;
        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            if (c == '"')
            {
                quoted = !quoted;
                continue;
            }
            if (quoted)
            {
                continue;
            }
            if (c == '(')
            {
                depth++;
                continue;
            }
            if (c == ')')
            {
                depth--;
                continue;
            }
            if (depth != 0)
            {
                continue;
            }
            foreach (var candidate in DialogueConditions.Operators)
            {
                if (i + candidate.Length <= source.Length
                    && string.CompareOrdinal(source, i, candidate, 0, candidate.Length) == 0)
                {
                    op = candidate;
                    return i;
                }
            }
        }
        return -1;
    }

    // Is `word` present as a whole word outside any quoted argument? Quotes
    // matter: a flag named "this is fine" must not read as Yarn's `is` operator.
    private static bool ContainsWordOutsideQuotes(string source, string word)
    {
        var quoted = false;
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] == '"')
            {
                quoted = !quoted;
                continue;
            }
            if (quoted || i + word.Length > source.Length)
            {
                continue;
            }
            if (string.Compare(source, i, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) != 0)
            {
                continue;
            }
            if (!IsWordBoundary(source, i - 1) || !IsWordBoundary(source, i + word.Length))
            {
                continue;
            }
            return true;
        }
        return false;
    }

    // Off either end of the string counts as a boundary; letters, digits and '_'
    // do not, so "or" doesn't match inside "party_has_room".
    private static bool IsWordBoundary(string source, int index)
    {
        if (index < 0 || index >= source.Length)
        {
            return true;
        }
        var c = source[index];
        return !char.IsLetterOrDigit(c) && c != '_';
    }

    // The first of `chars` that isn't inside a quoted argument, or -1.
    private static int IndexOutsideQuotes(string source, string chars)
    {
        var quoted = false;
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] == '"')
            {
                quoted = !quoted;
                continue;
            }
            if (!quoted && chars.IndexOf(source[i]) >= 0)
            {
                return i;
            }
        }
        return -1;
    }

    private static string[] Append(string[] args, string op, string value)
    {
        var result = new string[args.Length + 2];
        Array.Copy(args, result, args.Length);
        result[^2] = op;
        result[^1] = value;
        return result;
    }

    // Comma-separated call arguments, honoring double quotes. `f()` has none.
    private static string[] SplitConditionArgs(string inside)
    {
        if (string.IsNullOrWhiteSpace(inside))
        {
            return Array.Empty<string>();
        }
        var result = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        foreach (var c in inside)
        {
            if (c == '"')
            {
                quoted = !quoted;
                continue;
            }
            if (c == ',' && !quoted)
            {
                result.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }
            current.Append(c);
        }
        result.Add(current.ToString().Trim());
        return result.ToArray();
    }

    private sealed class Compiler
    {
        // A link that still names a Yarn node title rather than a graph node id.
        // Jumps can point forward, and a title's head node isn't known until its
        // body is compiled, so they are patched in a second pass. The control
        // character keeps a marker from ever colliding with an authored id.
        private const string JumpMarker = "jump:";

        private readonly DialogueGraph graph;
        private readonly List<string> problems;
        // Titles seen in the file, so a generated id never shadows one and a
        // jump can be checked against them.
        private readonly HashSet<string> titles = new(StringComparer.Ordinal);
        // Title -> the id of the first node its body emits (or a jump marker, or
        // null when the body is empty). What a <<jump>> to that title resolves
        // to.
        private readonly Dictionary<string, string> heads = new(StringComparer.Ordinal);
        private readonly HashSet<string> emitted = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> counters = new(StringComparer.Ordinal);

        public Compiler(DialogueGraph graph, List<string> problems)
        {
            this.graph = graph;
            this.problems = problems;
        }

        public void Run(YarnScript script)
        {
            var entryTitle = (string)null;
            foreach (var node in script.Nodes)
            {
                if (string.IsNullOrEmpty(node.Title))
                {
                    continue;
                }
                if (!titles.Add(node.Title))
                {
                    problems.Add($"line {node.Line}: a second node is titled '{node.Title}'; keeping the first.");
                    continue;
                }
                if (entryTitle == null && IsEntry(node))
                {
                    entryTitle = node.Title;
                }
            }
            entryTitle ??= FirstTitle(script);

            foreach (var node in script.Nodes)
            {
                if (string.IsNullOrEmpty(node.Title) || heads.ContainsKey(node.Title))
                {
                    continue;
                }
                heads[node.Title] = CompileFrom(node.Body, 0, null, null, node.Title);
            }

            graph.EntryNodeId = entryTitle == null ? null : Resolve(HeadOf(entryTitle));
            PatchJumps();
        }

        private static bool IsEntry(YarnNode node)
        {
            var value = node.Header(YarnParser.EntryHeader);
            return value != null && Array.IndexOf(TrueHeaders, value.Trim().ToLowerInvariant()) >= 0;
        }

        private static string FirstTitle(YarnScript script)
        {
            foreach (var node in script.Nodes)
            {
                if (!string.IsNullOrEmpty(node.Title))
                {
                    return node.Title;
                }
            }
            return null;
        }

        private string HeadOf(string title) =>
            heads.TryGetValue(title, out var head) ? head : null;

        // Compiles the statements from index onward into graph nodes and returns
        // the id (or jump marker) of the first one — i.e. what the statement
        // before it should link to. continuation is where the block's tail
        // flows; carrier is the most recent line node, which trailing commands
        // and option groups attach to.
        private string CompileFrom(
            List<YarnStatement> body, int index, string continuation, DialogueNode carrier, string title)
        {
            var commands = TakeCommands(body, ref index);
            if (index >= body.Count)
            {
                Attach(commands, carrier);
                return continuation;
            }

            switch (body[index])
            {
                case YarnLine line:
                {
                    var node = NewNode(title);
                    node.Speaker = line.Speaker;
                    node.Text = line.Text;
                    var effects = ToEffects(commands);
                    if (effects.Count > 0)
                    {
                        node.OnShownEffects = effects;
                    }
                    var next = index + 1;
                    if (next < body.Count && body[next] is YarnOptionGroup group)
                    {
                        // The choice menu belongs to this line; whatever follows
                        // the group is where an option with no target flows.
                        var after = CompileFrom(body, next + 1, continuation, node, title);
                        node.Choices = CompileOptions(group, after, title);
                    }
                    else
                    {
                        node.NextNodeId = CompileFrom(body, next, continuation, node, title);
                    }
                    return node.Id;
                }

                case YarnJump jump:
                    Attach(commands, carrier);
                    ReportUnreachable(body, index + 1, jump.Line);
                    return string.IsNullOrEmpty(jump.Target) ? null : $"{JumpMarker}{jump.Line}:{jump.Target}";

                case YarnStop stop:
                    Attach(commands, carrier);
                    ReportUnreachable(body, index + 1, stop.Line);
                    return null;

                case YarnIf branch:
                {
                    Attach(commands, carrier);
                    // Allocated before the clause bodies so the router takes the
                    // node title when the body opens with an <<if>>.
                    var router = NewNode(title);
                    router.Branches = new List<DialogueBranch>();
                    var after = CompileFrom(body, index + 1, continuation, carrier, title);
                    foreach (var clause in branch.Clauses)
                    {
                        router.Branches.Add(new DialogueBranch
                        {
                            When = ParseCondition(clause.Condition, clause.Line, problems),
                            ToNodeId = CompileFrom(clause.Body, 0, after, carrier, title),
                        });
                    }
                    if (branch.ElseBody != null)
                    {
                        // <<else>> is the router's unconditional last branch.
                        router.Branches.Add(new DialogueBranch
                        {
                            ToNodeId = CompileFrom(branch.ElseBody, 0, after, carrier, title),
                        });
                    }
                    else
                    {
                        // No <<else>>: an unmatched router falls through to
                        // whatever follows <<endif>>.
                        router.NextNodeId = after;
                    }
                    return router.Id;
                }

                case YarnOptionGroup group:
                    // Options render as the choices of the line above them, so a
                    // group with no line to attach to has nowhere to live.
                    problems.Add(
                        $"line {group.Line}: options must follow a line of dialogue "
                        + "(the line whose choices they are).");
                    Attach(commands, carrier);
                    return continuation;

                default:
                    return continuation;
            }
        }

        private List<DialogueChoiceData> CompileOptions(YarnOptionGroup group, string after, string title)
        {
            var choices = new List<DialogueChoiceData>();
            foreach (var option in group.Options)
            {
                var choice = new DialogueChoiceData { Label = option.Label };
                if (option.Condition != null)
                {
                    choice.Visible = ParseCondition(option.Condition, option.Line, problems);
                }
                // Commands at the top of the body fire when the option is
                // picked, before its target shows.
                var index = 0;
                var effects = ToEffects(TakeCommands(option.Body, ref index));
                if (effects.Count > 0)
                {
                    choice.Effects = effects;
                }
                choice.NextNodeId = CompileFrom(option.Body, index, after, null, title);
                choices.Add(choice);
            }
            return choices;
        }

        private static List<YarnCommand> TakeCommands(List<YarnStatement> body, ref int index)
        {
            var commands = new List<YarnCommand>();
            while (index < body.Count && body[index] is YarnCommand command)
            {
                commands.Add(command);
                index++;
            }
            return commands;
        }

        // Commands with no following line attach to the line they were written
        // under, so `<<take_item 1>>` after the last line of a node still runs.
        private void Attach(List<YarnCommand> commands, DialogueNode carrier)
        {
            if (commands.Count == 0)
            {
                return;
            }
            // Checked even when the commands have nowhere to go, so a misplaced
            // command's verb and arguments are reported too.
            var effects = ToEffects(commands);
            if (carrier == null)
            {
                foreach (var command in commands)
                {
                    problems.Add(
                        $"line {command.Line}: <<{command.Verb}>> has no line of dialogue to run on — "
                        + "put it above a line, or inside an option's body.");
                }
                return;
            }
            if (effects.Count == 0)
            {
                return;
            }
            carrier.OnShownEffects ??= new List<EffectRef>();
            carrier.OnShownEffects.AddRange(effects);
        }

        private List<EffectRef> ToEffects(List<YarnCommand> commands)
        {
            var effects = new List<EffectRef>();
            foreach (var command in commands)
            {
                var effect = new EffectRef { Id = command.Verb, Args = command.Args };
                if (Array.IndexOf(DialogueEffects.Ids, command.Verb) < 0)
                {
                    problems.Add(
                        $"line {command.Line}: unknown command '<<{command.Verb}>>'. "
                        + $"Known: {string.Join(", ", DialogueEffects.Ids)}.");
                }
                else if (DialogueEffects.Validate(effect) is { } reason)
                {
                    problems.Add($"line {command.Line}: {reason}.");
                }
                effects.Add(effect);
            }
            return effects;
        }

        private void ReportUnreachable(List<YarnStatement> body, int index, int line)
        {
            if (index < body.Count)
            {
                problems.Add($"line {line}: statements after this are unreachable and were dropped.");
            }
        }

        private DialogueNode NewNode(string title)
        {
            string id;
            if (!counters.ContainsKey(title))
            {
                counters[title] = 1;
                id = title;
            }
            else
            {
                var n = counters[title];
                do
                {
                    n++;
                    id = $"{title}__{n}";
                }
                while (emitted.Contains(id) || titles.Contains(id));
                counters[title] = n;
            }
            emitted.Add(id);
            var node = new DialogueNode { Id = id };
            graph.Nodes.Add(node);
            return node;
        }

        // Second pass: turn every jump marker into the graph node id the target
        // title's body starts with.
        private void PatchJumps()
        {
            foreach (var node in graph.Nodes)
            {
                node.NextNodeId = Resolve(node.NextNodeId);
                foreach (var choice in node.Choices ?? new List<DialogueChoiceData>())
                {
                    choice.NextNodeId = Resolve(choice.NextNodeId);
                }
                foreach (var branch in node.Branches ?? new List<DialogueBranch>())
                {
                    branch.ToNodeId = Resolve(branch.ToNodeId);
                }
            }
        }

        private string Resolve(string link)
        {
            // A jump to a node that itself opens with a jump chains, so follow
            // the markers (bounded, in case a file jumps in a circle).
            for (var guard = 0; link != null && link.StartsWith(JumpMarker, StringComparison.Ordinal); guard++)
            {
                var rest = link[JumpMarker.Length..];
                var colon = rest.IndexOf(':');
                var line = rest[..colon];
                var title = rest[(colon + 1)..];
                if (!titles.Contains(title))
                {
                    problems.Add($"line {line}: <<jump {title}>> names a node this file doesn't have.");
                    return null;
                }
                if (guard >= graph.Nodes.Count + 1)
                {
                    problems.Add($"line {line}: <<jump {title}>> loops back through jumps forever.");
                    return null;
                }
                link = HeadOf(title);
            }
            return link;
        }
    }
}
