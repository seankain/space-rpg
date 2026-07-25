using System;
using System.Collections.Generic;
using System.Linq;

// The result of completing a console line: the (possibly) extended line and the
// candidates that matched the token being completed.
public readonly struct Completion
{
    public Completion(string line, IReadOnlyList<string> matches)
    {
        Line = line;
        Matches = matches;
    }

    public string Line { get; }
    public IReadOnlyList<string> Matches { get; }
}

// Tab-completion of the final token on a console line (in-game-editor plan
// Phase 6). Engine-free and unit-tested; DevConsole supplies the candidate list
// for the position being completed (command names, item/quest ids, npc ids) and
// applies the returned line to the input field.
public static class CommandCompleter
{
    public static Completion CompleteLine(string line, IReadOnlyList<string> candidates)
    {
        line ??= "";
        var lastSpace = line.LastIndexOf(' ');
        var head = lastSpace >= 0 ? line[..(lastSpace + 1)] : "";
        var token = lastSpace >= 0 ? line[(lastSpace + 1)..] : line;

        var matches = candidates
            .Where(c => c.StartsWith(token, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 0)
        {
            return new Completion(line, matches);
        }
        // One match completes fully and adds a space to move on; several extend
        // the token only as far as they all agree (their common prefix).
        var completedToken = matches.Count == 1 ? matches[0] : LongestCommonPrefix(matches);
        var completedLine = head + completedToken + (matches.Count == 1 ? " " : "");
        return new Completion(completedLine, matches);
    }

    private static string LongestCommonPrefix(IReadOnlyList<string> values)
    {
        var prefix = values[0];
        foreach (var value in values.Skip(1))
        {
            var length = 0;
            while (length < prefix.Length && length < value.Length
                && char.ToLowerInvariant(prefix[length]) == char.ToLowerInvariant(value[length]))
            {
                length++;
            }
            prefix = prefix[..length];
            if (prefix.Length == 0)
            {
                break;
            }
        }
        return prefix;
    }
}
