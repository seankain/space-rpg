using System;

// Where a conversation lives on disk, and what the editor's save writes.
// Engine-free (no res:// I/O) so the save path's decisions are unit-tested;
// DialogueEditing does the actual FileAccess work with what this returns.
//
// The one rule worth stating: **the editor saves Yarn** (npc-dialogue-yarn.md
// — Yarn is the authoring format). A conversation still loads from either
// <id>.dialogue.json or <id>.yarn, but saving one writes .yarn either way, so
// editing a legacy JSON conversation in-game converts it instead of keeping the
// game's content split across two formats. The JSON it came from is the file
// that save replaces (ReplacedJsonPath) and DialogueEditing deletes, because a
// conversation lives in exactly one file — a leftover .json would claim the same
// id and race the new .yarn in the catalog.
public static class DialogueFiles
{
    public const string JsonSuffix = ".dialogue.json";
    public const string YarnSuffix = YarnGraphCompiler.FileSuffix;

    // The file a save writes: always Yarn, beside the file the conversation was
    // loaded from (fallbackDirectory for one that has never been saved).
    public static string SavePath(DialogueGraph graph, string fallbackDirectory) =>
        Join(SaveDirectory(graph, fallbackDirectory), (graph?.Id ?? "") + YarnSuffix);

    // A conversation is rewritten where it already lives, so one authored in a
    // Resources/Dialogue subfolder stays there instead of being duplicated into
    // the root on its first save.
    public static string SaveDirectory(DialogueGraph graph, string fallbackDirectory)
    {
        var directory = DirectoryOf(graph?.SourcePath);
        return string.IsNullOrEmpty(directory) ? fallbackDirectory : directory;
    }

    // The .dialogue.json this save converts away, or null when there is none:
    // the conversation's own source file, when it is JSON and still claims the
    // id being written. A "save as" to a new id (`dialogue save <other-id>`)
    // returns null — that writes a second conversation and leaves the original
    // where it is.
    public static string ReplacedJsonPath(DialogueGraph graph)
    {
        var path = graph?.SourcePath;
        if (string.IsNullOrEmpty(path) || !path.EndsWith(JsonSuffix, StringComparison.Ordinal))
        {
            return null;
        }
        return IdFromPath(path) == graph.Id ? path : null;
    }

    // The conventional JSON path for an id, used to spot a rival file left in
    // the directory (one this save did not come from, so it is not deleted).
    public static string JsonPath(string directory, string id) => Join(directory, id + JsonSuffix);

    // The file stem with its dialogue suffix removed — the conversation id, the
    // same way the catalog derives it (a .yarn id is its file stem).
    public static string IdFromPath(string path)
    {
        var name = FileName(path);
        return name.EndsWith(JsonSuffix, StringComparison.Ordinal)
            ? name[..^JsonSuffix.Length]
            : YarnGraphCompiler.IdFromFileName(name);
    }

    // res:// paths are always '/'-separated, so this stays away from Path.* and
    // its platform separators.
    public static string DirectoryOf(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }
        var slash = path.LastIndexOf('/');
        if (slash < 0)
        {
            return null;
        }
        // "res://x" — keep the scheme's second slash, or the directory reads
        // back as "res:/".
        return slash > 0 && path[slash - 1] == '/' ? path[..(slash + 1)] : path[..slash];
    }

    private static string FileName(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "";
        }
        var slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
    }

    private static string Join(string directory, string name)
    {
        if (string.IsNullOrEmpty(directory))
        {
            return name;
        }
        return directory.EndsWith("/", StringComparison.Ordinal)
            ? directory + name
            : directory + "/" + name;
    }
}
