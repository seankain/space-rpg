using Godot;
using System.Collections.Generic;
using System.Linq;

// The Godot-facing side of dialogue editing (dialogue-editor plan Phase 4):
// writing an edited graph to res://Resources/Dialogue and pointing an NPC's
// role at a conversation by re-saving its .tres. Debug-build gated and writing
// committed res:// files, exactly like NPC placement (EditorPlacement.Save) and
// the map baker — release builds don't author content. The engine-free editing
// and validation live in Scripts/Dialogue; this is just the file I/O.
public static class DialogueEditing
{
    // Validate then write the graph to its <id>.yarn and invalidate the catalog
    // so the next conversation plays the edit live. Errors block the save;
    // warnings (orphans) are reported but allowed.
    //
    // Every save writes Yarn — that is the authoring format (npc-dialogue-yarn.md),
    // and the editor is one of the two ways content gets written, so it has no
    // business producing a second format. A conversation still *loads* from a
    // legacy <id>.dialogue.json; saving it converts it, which means deleting the
    // JSON, since two files claiming one id is an error the catalog reports.
    public static CommandResult Save(DialogueGraph graph)
    {
        if (graph == null)
        {
            return CommandResult.Fail("No dialogue open. Use 'dialogue open <id>' or 'dialogue new <id>' first.");
        }
        if (string.IsNullOrEmpty(graph.Id))
        {
            return CommandResult.Fail("Dialogue has no id.");
        }
        if (!IsEditorBuild())
        {
            return CommandResult.Fail(
                "Saving dialogue writes to res:// and only works in the editor/debug player "
                + "(like the map baker). Author here, then commit the .yarn.");
        }

        var issues = DialogueValidation.Validate(graph);
        if (DialogueValidation.HasErrors(issues))
        {
            var errors = issues
                .Where(i => i.Severity == DialogueSeverity.Error)
                .Take(6)
                .Select(i => "  - " + i.Message);
            return CommandResult.Fail("Can't save — fix these first:\n" + string.Join("\n", errors));
        }

        // A conversation is rewritten where it already lives; a brand-new one
        // lands in the dialogue root.
        var directory = DialogueFiles.SaveDirectory(graph, DialogueCatalog.DialogueDirectory);
        if (!DirAccess.DirExistsAbsolute(directory))
        {
            var mkdir = DirAccess.MakeDirRecursiveAbsolute(directory);
            if (mkdir != Error.Ok)
            {
                return CommandResult.Fail($"Could not create '{directory}': {mkdir}.");
            }
        }

        var path = DialogueFiles.SavePath(graph, DialogueCatalog.DialogueDirectory);
        // Notes are what the file can't say for itself: anything Yarn couldn't
        // express, plus trouble with the .json this save replaces.
        var notes = new List<string>();
        var contents = YarnGraphWriter.Write(graph, notes);
        {
            using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
            if (file == null)
            {
                return CommandResult.Fail($"Could not write '{path}': {FileAccess.GetOpenError()}.");
            }
            file.StoreString(contents);
        }
        // File closed (using scope ended) before the rescan reads it back.
        var converted = RemoveReplacedJson(graph, directory, notes);
        graph.SourceFormat = DialogueSourceFormat.Yarn;
        graph.SourcePath = path;
        DialogueCatalog.Invalidate();

        var warnings = issues.Count(i => i.Severity == DialogueSeverity.Warning) + notes.Count;
        var note = warnings > 0 ? $"  ({warnings} warning{(warnings == 1 ? "" : "s")})" : "";
        // Every note is named outright — the author is standing in front of the
        // console and the file is already written.
        var wrote = notes.Count > 0
            ? "\n" + string.Join("\n", notes.Select(p => "  - " + p))
            : "";
        return CommandResult.Ok(
            $"Saved '{graph.Id}' to {path}.{converted}{note} Replay the NPC to see it live.{wrote}");
    }

    // Converting a JSON conversation to Yarn: the file it came from goes, or the
    // catalog would see two files claiming one id. Only the graph's own source
    // is deleted — a rival .json that this conversation was not loaded from is
    // reported instead, because deleting someone else's file is not this
    // command's call. Returns the note for the success line.
    private static string RemoveReplacedJson(DialogueGraph graph, string directory, List<string> notes)
    {
        var replaced = DialogueFiles.ReplacedJsonPath(graph);
        if (replaced != null && FileAccess.FileExists(replaced))
        {
            var error = DirAccess.RemoveAbsolute(replaced);
            if (error != Error.Ok)
            {
                notes.Add(
                    $"could not delete the old '{replaced}': {error}. Two files now claim "
                    + $"'{graph.Id}' — delete it by hand.");
                return "";
            }
            return $" Converted to Yarn — deleted the old {replaced}.";
        }

        var rival = DialogueFiles.JsonPath(directory, graph.Id);
        if (rival != replaced && FileAccess.FileExists(rival))
        {
            notes.Add(
                $"'{rival}' also claims '{graph.Id}'. A conversation lives in one file — "
                + "delete it so the catalog loads this .yarn.");
        }
        return "";
    }

    // Point an NPC's first role at a conversation and re-save its definition
    // .tres (the same ResourceSaver path NPC placement uses).
    public static CommandResult Assign(string npcId, string dialogueId)
    {
        if (!IsEditorBuild())
        {
            return CommandResult.Fail(
                "Assigning a dialogue re-saves a .tres and only works in the editor/debug player.");
        }
        if (DialogueCatalog.Get(dialogueId) == null)
        {
            return CommandResult.Fail($"No dialogue '{dialogueId}'. Try 'dialogue list'.");
        }
        var definition = NpcDatabase.Get(npcId);
        if (definition == null)
        {
            return CommandResult.Fail($"No NPC '{npcId}'. Try 'list npcs'.");
        }
        var role = definition.Roles?.FirstOrDefault(r => r != null);
        if (role == null)
        {
            return CommandResult.Fail($"NPC '{npcId}' has no roles to assign a dialogue to.");
        }
        var path = definition.ResourcePath;
        if (string.IsNullOrEmpty(path))
        {
            return CommandResult.Fail($"NPC '{npcId}' has no source .tres to save.");
        }

        role.DialogueId = dialogueId;
        var error = ResourceSaver.Save(definition, path);
        if (error != Error.Ok)
        {
            return CommandResult.Fail($"Saving '{path}' failed: {error}.");
        }
        NpcDatabase.Invalidate();
        return CommandResult.Ok(
            $"Assigned '{dialogueId}' to {npcId}'s {role.GetType().Name}. Saved {path}. Reload the NPC to see it.");
    }

    // Editor-only node positions for the graph canvas (Phase 5), read from the
    // sibling <id>.layout.json. Missing or unreadable layout is fine (the canvas
    // auto-arranges), so this never errors — it returns null.
    public static DialogueLayout LoadLayout(string id)
    {
        var path = LayoutPath(id);
        if (!FileAccess.FileExists(path))
        {
            return null;
        }
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            return null;
        }
        try
        {
            return DialogueLayoutSerialization.FromJson(file.GetAsText());
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    // Writes the canvas layout beside the dialogue file. Debug-gated like Save,
    // but a layout failure is reported softly — it never blocks authoring.
    public static CommandResult SaveLayout(DialogueLayout layout)
    {
        if (layout == null || string.IsNullOrEmpty(layout.Id))
        {
            return CommandResult.Fail("No layout to save.");
        }
        if (!IsEditorBuild())
        {
            return CommandResult.Fail("Saving layout writes to res:// and only works in the editor/debug player.");
        }
        var path = LayoutPath(layout.Id);
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (file == null)
        {
            return CommandResult.Fail($"Could not write '{path}': {FileAccess.GetOpenError()}.");
        }
        file.StoreString(DialogueLayoutSerialization.ToJson(layout));
        return CommandResult.Ok($"Saved layout to {path}.");
    }

    private static string LayoutPath(string id) => $"{DialogueCatalog.DialogueDirectory}/{id}.layout.json";

    private static bool IsEditorBuild() => OS.IsDebugBuild() || OS.HasFeature("editor");
}
