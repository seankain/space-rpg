using Godot;
using System.Collections.Generic;

// Discovers and caches the serialized conversations, keyed by DialogueGraph.Id
// (dialogue-editor plan Phase 1). Same manifest-free, directory-is-the-content
// pattern as NpcDatabase/ChunkManager: authoring a conversation is just saving
// a <id>.dialogue.json — or a <id>.yarn (npc-dialogue-yarn.md Phase 1) — under
// Resources/Dialogue, no index to drift. The graphs are read once on first use
// and cached; Invalidate() drops the cache so the in-game editor's save can make
// an edited conversation play live (Phase 4).
//
// Both formats produce the same DialogueGraph: .yarn files are compiled by
// YarnGraphCompiler at load, so writers can author in Yarn while the runtime,
// the editor, and the effect/condition vocabulary stay as they are.
//
// Godot-facing (DirAccess + FileAccess so exported .pck builds resolve res://),
// so it stays out of the engine-free xunit build; the JSON round-trip and the
// Yarn compile themselves live in DialogueSerialization/YarnGraphCompiler,
// which the tests exercise directly.
public static class DialogueCatalog
{
    public const string DialogueDirectory = "res://Resources/Dialogue";
    public const string FileSuffix = ".dialogue.json";
    public const string YarnSuffix = YarnGraphCompiler.FileSuffix;

    private static Dictionary<string, DialogueGraph> graphs;

    private static Dictionary<string, DialogueGraph> Graphs
    {
        get
        {
            if (graphs == null)
            {
                graphs = new Dictionary<string, DialogueGraph>();
                LoadDirectory(DialogueDirectory);
            }
            return graphs;
        }
    }

    public static IReadOnlyCollection<DialogueGraph> All => Graphs.Values;

    public static IEnumerable<string> Ids => Graphs.Keys;

    public static DialogueGraph Get(string id) =>
        id != null && Graphs.TryGetValue(id, out var graph) ? graph : null;

    // Drops the cache so the next access rescans; the editor calls this after
    // saving an edited or new conversation (dialogue-editor plan Phase 4).
    public static void Invalidate() => graphs = null;

    private static void LoadDirectory(string directoryPath)
    {
        using var dir = DirAccess.Open(directoryPath);
        if (dir == null)
        {
            // A missing directory is fine before any conversation is authored;
            // the catalog is simply empty until one lands.
            return;
        }
        var subdirectories = dir.GetDirectories();
        System.Array.Sort(subdirectories, System.StringComparer.Ordinal);
        foreach (var subdirectory in subdirectories)
        {
            LoadDirectory($"{directoryPath}/{subdirectory}");
        }
        // Sorted so which file wins an id clash (a .json and a .yarn claiming
        // the same conversation) does not depend on directory listing order.
        var files = dir.GetFiles();
        System.Array.Sort(files, System.StringComparer.Ordinal);
        foreach (var file in files)
        {
            // Exported builds list resource files with a .remap suffix.
            var name = file.EndsWith(".remap") ? file[..^".remap".Length] : file;
            if (name.EndsWith(FileSuffix))
            {
                Load($"{directoryPath}/{name}");
            }
            else if (name.EndsWith(YarnSuffix))
            {
                LoadYarn($"{directoryPath}/{name}");
            }
        }
    }

    private static void Load(string path)
    {
        var json = ReadText(path);
        if (json == null)
        {
            return;
        }

        DialogueGraph graph;
        try
        {
            graph = DialogueSerialization.FromJson(json);
        }
        catch (System.Text.Json.JsonException e)
        {
            GD.PushError($"Dialogue file '{path}' is not valid JSON: {e.Message}");
            return;
        }
        if (graph == null || string.IsNullOrEmpty(graph.Id))
        {
            GD.PushError($"Dialogue file '{path}' has no Id.");
            return;
        }
        graph.SourceFormat = DialogueSourceFormat.Json;
        Add(graph, path);
    }

    // A .yarn file is one conversation whose id is the file stem (there is no
    // Id field to read). Compile problems are reported and the graph is still
    // cached, so a half-broken script is visible in the editor's validator
    // rather than vanishing from the catalog.
    private static void LoadYarn(string path)
    {
        var source = ReadText(path);
        if (source == null)
        {
            return;
        }
        var id = YarnGraphCompiler.IdFromFileName(path);
        var problems = new List<string>();
        var graph = YarnGraphCompiler.Compile(id, source, problems);
        foreach (var problem in problems)
        {
            GD.PushError($"{path}: {problem}");
        }
        Add(graph, path);
    }

    private static void Add(DialogueGraph graph, string path)
    {
        if (!graphs.TryAdd(graph.Id, graph))
        {
            GD.PushError(
                $"Duplicate dialogue id '{graph.Id}' at '{path}'; keeping the first. "
                + "A conversation lives in one file — either the .dialogue.json or the .yarn.");
        }
    }

    private static string ReadText(string path)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PushError($"Dialogue file '{path}' could not be read: {FileAccess.GetOpenError()}");
            return null;
        }
        var text = file.GetAsText();
        if (string.IsNullOrEmpty(text))
        {
            GD.PushError($"Dialogue file '{path}' is empty.");
            return null;
        }
        return text;
    }
}
