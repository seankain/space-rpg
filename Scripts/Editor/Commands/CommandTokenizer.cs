using System.Collections.Generic;
using System.Text;

// Splits a raw console line into a command name plus arguments (in-game-editor
// plan Phase 1). Whitespace separates tokens; double quotes group a token so
// display names with spaces survive (e.g. give "Vac-Suit Plating" 2). Engine-
// free and unit-tested.
public static class CommandTokenizer
{
    public static List<string> Tokenize(string line)
    {
        var tokens = new List<string>();
        if (string.IsNullOrEmpty(line))
        {
            return tokens;
        }
        var current = new StringBuilder();
        var inQuotes = false;
        // Tracks whether we've begun a token, so an empty quoted string ("")
        // still produces one empty token and trailing spaces don't add blanks.
        var hasToken = false;
        foreach (var c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }
            }
            else
            {
                current.Append(c);
                hasToken = true;
            }
        }
        if (hasToken)
        {
            tokens.Add(current.ToString());
        }
        return tokens;
    }
}
