using Godot;
using System.Collections.Generic;

// Discovers and caches the serialized conversations, keyed by DialogueGraph.Id
// (dialogue-editor plan Phase 1). Same manifest-free, directory-is-the-content
// pattern as NpcDatabase/ChunkManager: authoring a conversation is just saving
// a <id>.dialogue.json under Resources/Dialogue, no index to drift. The graphs
// are read once on first use and cached; Invalidate() drops the cache so the
// in-game editor's save can make an edited conversation play live (Phase 4).
//
// Godot-facing (DirAccess + FileAccess so exported .pck builds resolve res://),
// so it stays out of the engine-free xunit build; the JSON round-trip itself
// lives in DialogueSerialization, which the tests exercise directly.
public static class DialogueCatalog
{
    public const string DialogueDirectory = "res://Resources/Dialogue";
    public const string FileSuffix = ".dialogue.json";

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
        foreach (var subdirectory in dir.GetDirectories())
        {
            LoadDirectory($"{directoryPath}/{subdirectory}");
        }
        foreach (var file in dir.GetFiles())
        {
            // Exported builds list resource files with a .remap suffix.
            var name = file.EndsWith(".remap") ? file[..^".remap".Length] : file;
            if (!name.EndsWith(FileSuffix))
            {
                continue;
            }
            Load($"{directoryPath}/{name}");
        }
    }

    private static void Load(string path)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PushError($"Dialogue file '{path}' could not be read: {FileAccess.GetOpenError()}");
            return;
        }
        var json = file.GetAsText();
        if (string.IsNullOrEmpty(json))
        {
            GD.PushError($"Dialogue file '{path}' is empty.");
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
        if (!graphs.TryAdd(graph.Id, graph))
        {
            GD.PushError($"Duplicate dialogue id '{graph.Id}' at '{path}'; keeping the first.");
        }
    }
}
