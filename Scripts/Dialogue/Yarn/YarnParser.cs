using System;
using System.Collections.Generic;
using System.Text;

// Parses Yarn Spinner source into a statement tree (npc-dialogue-yarn.md
// Phase 1). Engine-free like the rest of the dialogue layer, so the xunit build
// exercises it without Godot.
//
// This is a *subset* of Yarn Spinner's syntax — the part that maps onto this
// project's DialogueGraph: nodes with `title:` headers, speaker lines, option
// groups, `<<jump>>`/`<<stop>>`, `<<if>>/<<elseif>>/<<else>>/<<endif>>`, and
// custom commands (the DialogueEffects vocabulary). Everything here is valid
// Yarn Spinner syntax, so the .yarn files stay readable by the real toolchain
// (VS Code extension, and the upstream compiler if the project ever swaps this
// parser for it) — but Yarn features with no home in the graph model
// (`$variables`, expressions, `<<declare>>`, `{interpolation}`, `<<wait>>`) are
// rejected with a line-numbered problem instead of being silently dropped.
// YarnGraphCompiler turns this tree into a DialogueGraph; YarnGraphWriter turns
// a graph back into source.
public sealed class YarnScript
{
    public List<YarnNode> Nodes { get; } = new();
}

// One `title: ... --- body ===` node in a .yarn file. A file is one
// conversation (one DialogueGraph) and its nodes are the conversation's nodes.
public sealed class YarnNode
{
    public string Title { get; set; }
    // Extra `key: value` headers above the `---`. Only `entry` is meaningful
    // today (see YarnGraphCompiler); the rest are carried for future use
    // (localization groups, tags) rather than dropped.
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<YarnStatement> Body { get; set; } = new();
    public int Line { get; set; }

    public string Header(string key) => Headers.TryGetValue(key, out var value) ? value : null;
}

public abstract class YarnStatement
{
    // 1-based source line, so every problem the compiler reports can point at
    // the text the author wrote.
    public int Line { get; set; }
}

// A spoken line: `Speaker: text`, or bare text for narration.
public sealed class YarnLine : YarnStatement
{
    public string Speaker { get; set; }
    public string Text { get; set; }
}

// A custom command — `<<give_item 4 1>>` — i.e. one DialogueEffects verb.
// Control-flow commands are their own statement types instead.
public sealed class YarnCommand : YarnStatement
{
    public string Verb { get; set; }
    public string[] Args { get; set; } = Array.Empty<string>();
}

public sealed class YarnJump : YarnStatement
{
    public string Target { get; set; }
}

public sealed class YarnStop : YarnStatement
{
}

// A run of `->` options at the same indentation: the choice menu shown on the
// line above it.
public sealed class YarnOptionGroup : YarnStatement
{
    public List<YarnOption> Options { get; } = new();
}

public sealed class YarnOption
{
    public string Label { get; set; }
    // The `<<if ...>>` trailing the option, as written; null when ungated.
    public string Condition { get; set; }
    public List<YarnStatement> Body { get; set; } = new();
    public int Line { get; set; }
}

// `<<if>>/<<elseif>>/<<else>>/<<endif>>`. Clauses are ordered; ElseBody is null
// when the author wrote no `<<else>>` (flow then falls through to whatever
// follows `<<endif>>`).
public sealed class YarnIf : YarnStatement
{
    public List<YarnIfClause> Clauses { get; } = new();
    public List<YarnStatement> ElseBody { get; set; }
}

public sealed class YarnIfClause
{
    public string Condition { get; set; }
    public List<YarnStatement> Body { get; set; } = new();
    public int Line { get; set; }
}

public static class YarnParser
{
    public const string HeaderSeparator = "---";
    public const string NodeTerminator = "===";
    public const string TitleHeader = "title";
    // `entry: true` marks the conversation's entry node when it is not the
    // first node in the file (see YarnGraphCompiler.EntryTitle).
    public const string EntryHeader = "entry";

    // A `Name:` prefix only reads as a speaker when it is short — otherwise a
    // line that happens to contain a colon ("Wait: what?") would silently lose
    // its opening words. Longer prefixes stay part of the text; `\:` escapes a
    // colon explicitly.
    public const int MaxSpeakerLength = 40;

    private static readonly HashSet<string> ClauseKeywords = new(StringComparer.Ordinal)
    {
        "elseif", "else", "endif",
    };

    // Yarn constructs this subset has no representation for. Detected up front
    // so an author gets "not supported" on the offending line instead of a
    // conversation that quietly misbehaves.
    private static readonly string[] UnsupportedCommands =
    {
        "declare", "set", "wait", "call", "detour", "return", "once", "endonce", "line",
    };

    public static YarnScript Parse(string text, List<string> problems)
    {
        problems ??= new List<string>();
        var script = new YarnScript();
        if (text == null)
        {
            return script;
        }

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var index = 0;
        while (index < lines.Length)
        {
            var node = ReadNode(lines, ref index, problems);
            if (node != null)
            {
                script.Nodes.Add(node);
            }
        }
        return script;
    }

    // Reads one node: `key: value` headers, a `---`, body lines, a `===`.
    // Returns null at end of file (or when only blank lines remain).
    private static YarnNode ReadNode(string[] lines, ref int index, List<string> problems)
    {
        YarnNode node = null;
        // Headers.
        while (index < lines.Length)
        {
            var number = index + 1;
            var raw = StripComment(lines[index]);
            index++;
            var trimmed = raw.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }
            if (trimmed == HeaderSeparator)
            {
                if (node == null || string.IsNullOrEmpty(node.Title))
                {
                    problems.Add($"line {number}: node has no 'title:' header.");
                    node ??= new YarnNode { Line = number };
                }
                return ReadBody(lines, ref index, node, problems);
            }
            if (trimmed == NodeTerminator)
            {
                problems.Add($"line {number}: '{NodeTerminator}' without a node body (missing '{HeaderSeparator}').");
                continue;
            }
            var colon = trimmed.IndexOf(':');
            if (colon <= 0)
            {
                problems.Add($"line {number}: expected a 'key: value' header or '{HeaderSeparator}', got '{trimmed}'.");
                continue;
            }
            node ??= new YarnNode { Line = number };
            var key = trimmed[..colon].Trim();
            var value = trimmed[(colon + 1)..].Trim();
            if (key.Equals(TitleHeader, StringComparison.OrdinalIgnoreCase))
            {
                node.Title = value;
                node.Line = number;
            }
            node.Headers[key] = value;
        }
        if (node != null)
        {
            problems.Add($"line {node.Line}: node '{node.Title}' has headers but no '{HeaderSeparator}' body.");
        }
        return null;
    }

    private static YarnNode ReadBody(string[] lines, ref int index, YarnNode node, List<string> problems)
    {
        var body = new List<RawLine>();
        var terminated = false;
        while (index < lines.Length)
        {
            var number = index + 1;
            var raw = StripComment(lines[index]);
            index++;
            var trimmed = raw.Trim();
            if (trimmed == NodeTerminator)
            {
                terminated = true;
                break;
            }
            if (trimmed.Length == 0)
            {
                continue;
            }
            body.Add(new RawLine { Indent = IndentOf(raw), Text = trimmed, Number = number });
        }
        if (!terminated)
        {
            problems.Add($"line {node.Line}: node '{node.Title}' is not closed with '{NodeTerminator}'.");
        }

        var cursor = 0;
        node.Body = ParseBlock(body, ref cursor, 0, problems);
        // ParseBlock stops at a clause keyword it does not own; anything left
        // over is a stray <<else>>/<<endif>>.
        while (cursor < body.Count)
        {
            problems.Add($"line {body[cursor].Number}: unexpected '{body[cursor].Text}'.");
            cursor++;
        }
        return node;
    }

    private sealed class RawLine
    {
        public int Indent { get; set; }
        public string Text { get; set; }
        public int Number { get; set; }
    }

    // Statements at or deeper than minIndent, stopping at a dedent or at a
    // clause keyword (<<elseif>>/<<else>>/<<endif>>), which belongs to the
    // enclosing <<if>>.
    private static List<YarnStatement> ParseBlock(
        List<RawLine> lines, ref int index, int minIndent, List<string> problems)
    {
        var statements = new List<YarnStatement>();
        while (index < lines.Count)
        {
            var line = lines[index];
            if (line.Indent < minIndent)
            {
                break;
            }
            if (TryReadCommand(line.Text, out var verb, out var arguments, out var malformed))
            {
                if (ClauseKeywords.Contains(verb))
                {
                    break;
                }
                switch (verb)
                {
                    case "if":
                        statements.Add(ParseIf(lines, ref index, arguments, problems));
                        continue;
                    case "jump":
                        statements.Add(new YarnJump { Target = arguments.Trim(), Line = line.Number });
                        if (string.IsNullOrEmpty(arguments.Trim()))
                        {
                            problems.Add($"line {line.Number}: <<jump>> needs a node title.");
                        }
                        index++;
                        continue;
                    case "stop":
                        statements.Add(new YarnStop { Line = line.Number });
                        index++;
                        continue;
                    default:
                        statements.Add(ReadCustomCommand(verb, arguments, line, problems));
                        index++;
                        continue;
                }
            }
            if (malformed)
            {
                problems.Add($"line {line.Number}: unterminated command — expected '>>' at the end of '{line.Text}'.");
                index++;
                continue;
            }
            if (line.Text.StartsWith("->", StringComparison.Ordinal))
            {
                statements.Add(ParseOptionGroup(lines, ref index, line.Indent, problems));
                continue;
            }
            statements.Add(ParseLine(line, problems));
            index++;
        }
        return statements;
    }

    private static YarnIf ParseIf(
        List<RawLine> lines, ref int index, string condition, List<string> problems)
    {
        var start = lines[index];
        var statement = new YarnIf { Line = start.Number };
        var clauseIndent = start.Indent;
        var clauseCondition = condition.Trim();
        var clauseLine = start.Number;
        index++;

        while (true)
        {
            var body = ParseBlock(lines, ref index, clauseIndent, problems);
            if (clauseCondition == null)
            {
                statement.ElseBody = body;
            }
            else
            {
                statement.Clauses.Add(new YarnIfClause
                {
                    Condition = clauseCondition,
                    Body = body,
                    Line = clauseLine,
                });
            }

            if (index >= lines.Count
                || !TryReadCommand(lines[index].Text, out var verb, out var arguments, out _)
                || !ClauseKeywords.Contains(verb))
            {
                problems.Add($"line {start.Number}: <<if>> has no matching <<endif>>.");
                return statement;
            }

            clauseLine = lines[index].Number;
            switch (verb)
            {
                case "endif":
                    index++;
                    return statement;
                case "elseif":
                    clauseCondition = arguments.Trim();
                    if (statement.ElseBody != null)
                    {
                        problems.Add($"line {clauseLine}: <<elseif>> after <<else>>.");
                    }
                    break;
                default: // else
                    clauseCondition = null;
                    if (statement.ElseBody != null)
                    {
                        problems.Add($"line {clauseLine}: a second <<else>> in one <<if>>.");
                    }
                    break;
            }
            index++;
        }
    }

    private static YarnOptionGroup ParseOptionGroup(
        List<RawLine> lines, ref int index, int indent, List<string> problems)
    {
        var group = new YarnOptionGroup { Line = lines[index].Number };
        while (index < lines.Count
               && lines[index].Indent == indent
               && lines[index].Text.StartsWith("->", StringComparison.Ordinal))
        {
            var line = lines[index];
            index++;
            var label = line.Text[2..].Trim();
            var condition = ExtractTrailingIf(ref label, line.Number, problems);
            if (label.Length == 0)
            {
                problems.Add($"line {line.Number}: option has no text.");
            }
            group.Options.Add(new YarnOption
            {
                Label = Unescape(StripHashtags(label)),
                Condition = condition,
                // The option's body is whatever is indented under it.
                Body = ParseBlock(lines, ref index, indent + 1, problems),
                Line = line.Number,
            });
        }
        return group;
    }

    private static YarnLine ParseLine(RawLine line, List<string> problems)
    {
        var text = StripHashtags(line.Text);
        if (text.EndsWith(">>", StringComparison.Ordinal) && text.Contains("<<", StringComparison.Ordinal))
        {
            problems.Add(
                $"line {line.Number}: a command must be on its own line "
                + "(only an option's '<<if ...>>' may trail text).");
        }
        string speaker = null;
        var colon = UnescapedColon(text);
        if (colon > 0 && colon <= MaxSpeakerLength)
        {
            speaker = text[..colon].Trim();
            text = text[(colon + 1)..].Trim();
        }
        return new YarnLine
        {
            Speaker = speaker,
            Text = Unescape(text),
            Line = line.Number,
        };
    }

    private static YarnCommand ReadCustomCommand(
        string verb, string arguments, RawLine line, List<string> problems)
    {
        if (Array.IndexOf(UnsupportedCommands, verb) >= 0)
        {
            problems.Add(
                $"line {line.Number}: <<{verb}>> is not supported — this project's dialogue has no Yarn "
                + "variables or waits; use the game's commands (see DialogueEffects) and <<if>> functions.");
        }
        return new YarnCommand
        {
            Verb = verb,
            Args = SplitArguments(arguments),
            Line = line.Number,
        };
    }

    // `<<verb rest>>` on its own line. malformed is true for a line that opens
    // with `<<` but never closes.
    public static bool TryReadCommand(string text, out string verb, out string arguments, out bool malformed)
    {
        verb = null;
        arguments = null;
        malformed = false;
        if (!text.StartsWith("<<", StringComparison.Ordinal))
        {
            return false;
        }
        if (!text.EndsWith(">>", StringComparison.Ordinal) || text.Length < 4)
        {
            malformed = true;
            return false;
        }
        var inner = text[2..^2].Trim();
        if (inner.Length == 0)
        {
            malformed = true;
            return false;
        }
        var space = inner.IndexOf(' ');
        verb = space < 0 ? inner : inner[..space];
        arguments = space < 0 ? "" : inner[(space + 1)..].Trim();
        return true;
    }

    // Splits command arguments on whitespace, honoring double quotes so an
    // argument can contain spaces.
    public static string[] SplitArguments(string arguments)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(arguments))
        {
            return Array.Empty<string>();
        }
        var current = new StringBuilder();
        var quoted = false;
        var started = false;
        foreach (var c in arguments)
        {
            if (c == '"')
            {
                quoted = !quoted;
                started = true;
                continue;
            }
            if (!quoted && char.IsWhiteSpace(c))
            {
                if (started)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    started = false;
                }
                continue;
            }
            current.Append(c);
            started = true;
        }
        if (started)
        {
            result.Add(current.ToString());
        }
        return result.ToArray();
    }

    // Pulls a trailing `<<if condition>>` off an option label.
    private static string ExtractTrailingIf(ref string label, int number, List<string> problems)
    {
        var open = label.LastIndexOf("<<", StringComparison.Ordinal);
        if (open < 0)
        {
            return null;
        }
        if (!label.EndsWith(">>", StringComparison.Ordinal))
        {
            problems.Add($"line {number}: unterminated '<<' in option text.");
            return null;
        }
        var inner = label[(open + 2)..^2].Trim();
        label = label[..open].Trim();
        if (!inner.StartsWith("if", StringComparison.Ordinal))
        {
            problems.Add($"line {number}: only '<<if ...>>' may follow an option's text, got '<<{inner}>>'.");
            return null;
        }
        var condition = inner[2..].Trim();
        if (condition.Length == 0)
        {
            problems.Add($"line {number}: option's <<if>> has no condition.");
            return null;
        }
        return condition;
    }

    // `//` to end of line, outside double quotes.
    private static string StripComment(string line)
    {
        var quoted = false;
        for (var i = 0; i < line.Length - 1; i++)
        {
            if (line[i] == '"')
            {
                quoted = !quoted;
                continue;
            }
            if (!quoted && line[i] == '/' && line[i + 1] == '/')
            {
                return line[..i];
            }
        }
        return line;
    }

    // Yarn line tags (`#line:0a1b`, `#tag`) carry no meaning for the graph yet;
    // they are dropped rather than shown as dialogue text. Localization line
    // ids are Phase 5 work (npc-dialogue-yarn.md).
    private static string StripHashtags(string text)
    {
        var hash = -1;
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '"')
            {
                quoted = !quoted;
                continue;
            }
            if (!quoted && text[i] == '#' && (i == 0 || char.IsWhiteSpace(text[i - 1])))
            {
                hash = i;
                break;
            }
        }
        return hash < 0 ? text : text[..hash].TrimEnd();
    }

    // Index of the first colon not written as `\:`, or -1.
    private static int UnescapedColon(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\')
            {
                i++;
                continue;
            }
            if (text[i] == ':')
            {
                return i;
            }
        }
        return -1;
    }

    // Drops the backslash from an escaped character (`\:` -> `:`), the only
    // escape this subset needs.
    private static string Unescape(string text)
    {
        if (text == null || !text.Contains('\\'))
        {
            return text;
        }
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                builder.Append(text[i + 1]);
                i++;
                continue;
            }
            builder.Append(text[i]);
        }
        return builder.ToString();
    }

    private static int IndentOf(string line)
    {
        var indent = 0;
        foreach (var c in line)
        {
            if (c == ' ')
            {
                indent++;
            }
            else if (c == '\t')
            {
                indent += 4;
            }
            else
            {
                break;
            }
        }
        return indent;
    }
}
