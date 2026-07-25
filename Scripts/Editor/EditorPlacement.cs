using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Godot;

// The engine work behind the placement console verbs (in-game-editor plan
// Phase 3). Holds the "pending placement" — an NpcDefinition being authored and
// its live preview node — that place/nudge/rotate/savenpc/cancelplace share,
// and reuses NpcSpawner + NpcDatabase so authored NPCs load through the normal
// game path. Godot-facing, so it lives outside the engine-free Commands/ folder;
// the coordinate math it depends on is PlacementMath (engine-free, tested).
public static class EditorPlacement
{
    private const string RigDirectory = "res://Scenes/Characters/Rigs";
    private const string NpcSaveRoot = "res://Resources/Npcs";

    // The definition currently being authored (chunk/local already resolved),
    // its world position (kept for nudge and re-splitting), and its preview.
    private static NpcDefinition pending;
    private static Vector3 pendingWorld;
    private static Npc preview;

    // spawn <npcId>: a throwaway preview of an existing definition at the
    // cursor, run through NpcSpawner so its roles/behaviors fire normally. Not
    // saved and not tracked as the pending placement.
    public static CommandResult SpawnExisting(IReadOnlyList<string> args)
    {
        if (args.Count < 1)
        {
            return CommandResult.Fail("Usage: spawn <npcId>");
        }
        var definition = NpcDatabase.Get(args[0]);
        if (definition == null)
        {
            return CommandResult.Fail($"No NPC definition with id '{args[0]}'.");
        }
        if (!TryResolvePosition(Sublist(args, 1), out var world, out var posError))
        {
            return CommandResult.Fail(posError);
        }
        if (!TryLevelContext(out var context, out var ctxError))
        {
            return CommandResult.Fail(ctxError);
        }
        var npc = SpawnPreview(definition, world, definition.RotationDegreesY, context);
        return npc == null
            ? CommandResult.Fail($"'{args[0]}' did not spawn (a role vetoed it, e.g. already recruited/defeated).")
            : CommandResult.Ok($"Spawned preview of '{args[0]}' at {Format(world)}.");
    }

    // place <npcId> <displayName> [rig] [here | <x> <y> <z>]
    public static CommandResult Place(IReadOnlyList<string> args)
    {
        if (args.Count < 2)
        {
            return CommandResult.Fail("Usage: place <npcId> <displayName> [rig] [here | <x> <y> <z>]");
        }
        var npcId = args[0];
        var displayName = args[1];
        if (string.IsNullOrWhiteSpace(npcId))
        {
            return CommandResult.Fail("NPC id must not be empty.");
        }
        if (NpcDatabase.Get(npcId) != null)
        {
            return CommandResult.Fail($"An NPC with id '{npcId}' already exists. Pick a unique id.");
        }
        if (!TryLevelContext(out var context, out var ctxError))
        {
            return CommandResult.Fail(ctxError);
        }

        var rest = Sublist(args, 2);
        PackedScene rig = null;
        if (rest.Count > 0 && rest[0] != "here" && !TryFloat(rest[0], out _))
        {
            rig = ResolveRig(rest[0], out var rigError);
            if (rig == null)
            {
                return CommandResult.Fail(rigError);
            }
            rest = Sublist(rest, 1);
        }
        if (!TryResolvePosition(rest, out var world, out var posError))
        {
            return CommandResult.Fail(posError);
        }

        ClearPending();
        pending = BuildDefinition(npcId, displayName, rig, world, context);
        pendingWorld = world;
        preview = SpawnPreview(pending, world, 0f, context);
        return CommandResult.Ok(
            $"Placed '{npcId}' ({displayName}) at {Format(world)}, chunk ({pending.ChunkCoords.X}, {pending.ChunkCoords.Y}). "
            + "Use nudge/rotate to adjust, savenpc to keep it, cancelplace to discard.");
    }

    public static CommandResult Nudge(IReadOnlyList<string> args)
    {
        if (pending == null)
        {
            return CommandResult.Fail("Nothing placed. Use 'place' first.");
        }
        if (args.Count < 3 || !TryFloat(args[0], out var dx) || !TryFloat(args[1], out var dy) || !TryFloat(args[2], out var dz))
        {
            return CommandResult.Fail("Usage: nudge <dx> <dy> <dz>");
        }
        pendingWorld += new Vector3(dx, dy, dz);
        ApplyWorldToPending();
        return CommandResult.Ok(
            $"Moved '{pending.NpcId}' to {Format(pendingWorld)}, chunk ({pending.ChunkCoords.X}, {pending.ChunkCoords.Y}).");
    }

    public static CommandResult Rotate(IReadOnlyList<string> args)
    {
        if (pending == null)
        {
            return CommandResult.Fail("Nothing placed. Use 'place' first.");
        }
        if (args.Count < 1 || !TryFloat(args[0], out var degrees))
        {
            return CommandResult.Fail("Usage: rotate <degrees>");
        }
        pending.RotationDegreesY = degrees;
        if (IsPreviewAlive())
        {
            preview.RotationDegrees = new Vector3(0f, degrees, 0f);
        }
        return CommandResult.Ok($"Set '{pending.NpcId}' facing to {degrees:0.#}°.");
    }

    public static CommandResult Cancel()
    {
        if (pending == null)
        {
            return CommandResult.Fail("Nothing placed to cancel.");
        }
        var id = pending.NpcId;
        ClearPending();
        return CommandResult.Ok($"Discarded pending placement '{id}'.");
    }

    // savenpc: write the pending definition to Resources/Npcs/<Area>/<id>.tres
    // and refresh NpcDatabase so it streams in like any authored NPC.
    public static CommandResult Save()
    {
        if (pending == null)
        {
            return CommandResult.Fail("Nothing placed. Use 'place' first.");
        }
        if (!(OS.IsDebugBuild() || OS.HasFeature("editor")))
        {
            return CommandResult.Fail(
                "Saving NPCs writes to res:// and only works in the editor/debug player "
                + "(like the map baker). Author here, then commit the .tres.");
        }
        var area = PlacementMath.AreaFromScenePath(pending.SpawnScenePath);
        if (string.IsNullOrEmpty(area))
        {
            return CommandResult.Fail("Could not determine the area folder from the current level.");
        }
        var directory = $"{NpcSaveRoot}/{area}";
        if (!DirAccess.DirExistsAbsolute(directory))
        {
            var mkdir = DirAccess.MakeDirRecursiveAbsolute(directory);
            if (mkdir != Error.Ok)
            {
                return CommandResult.Fail($"Could not create '{directory}': {mkdir}.");
            }
        }
        var path = $"{directory}/{pending.NpcId}.tres";
        var error = ResourceSaver.Save(pending, path);
        if (error != Error.Ok)
        {
            return CommandResult.Fail($"Saving '{path}' failed: {error}.");
        }
        // The saved NPC now owns this world spot; the preview stays standing and
        // the pending slot clears so a re-save can't double-write.
        NpcDatabase.Invalidate();
        var savedId = pending.NpcId;
        pending = null;
        preview = null;
        pendingWorld = Vector3.Zero;
        return CommandResult.Ok($"Saved '{savedId}' to {path}. It will stream in with its chunk.");
    }

    // list npcs [area]: print known ids, optionally filtered by area folder.
    public static CommandResult ListNpcs(IReadOnlyList<string> args)
    {
        var filter = args.Count > 0 ? args[0] : null;
        var builder = new StringBuilder();
        var count = 0;
        foreach (var definition in NpcDatabase.All)
        {
            var area = PlacementMath.AreaFromScenePath(definition.SpawnScenePath);
            if (filter != null && !string.Equals(area, filter, System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            builder.Append('\n').Append("  ").Append(definition.NpcId)
                .Append("  (").Append(definition.DisplayName).Append(", ").Append(area).Append(')');
            count++;
        }
        var header = filter == null ? $"{count} NPC definition(s):" : $"{count} NPC definition(s) in '{filter}':";
        return CommandResult.Ok(header + builder);
    }

    // ---- helpers ----

    private static NpcDefinition BuildDefinition(string npcId, string displayName, PackedScene rig, Vector3 world, LevelContext context)
    {
        var definition = new NpcDefinition
        {
            NpcId = npcId,
            DisplayName = displayName,
            SpawnScenePath = context.LevelScenePath,
            RotationDegreesY = 0f,
            Behavior = NpcBehaviorMode.Stationary,
            Rig = rig,
        };
        ApplyWorld(definition, world, context.Chunked);
        return definition;
    }

    private static void ApplyWorldToPending()
    {
        ApplyWorld(pending, pendingWorld, IsChunked(pending));
        if (IsPreviewAlive())
        {
            preview.GlobalPosition = pendingWorld;
        }
    }

    // Interiors ignore ChunkCoords and author LocalPosition in scene-local space
    // (the scene root sits at the origin, so world == local); chunked levels
    // split the world position into a chunk plus a [-32, 32) local offset.
    private static void ApplyWorld(NpcDefinition definition, Vector3 world, bool chunked)
    {
        if (chunked)
        {
            var split = PlacementMath.SplitWorld(world.X, world.Y, world.Z);
            definition.ChunkCoords = new Vector2I(split.ChunkX, split.ChunkZ);
            definition.LocalPosition = new Vector3(split.LocalX, split.LocalY, split.LocalZ);
        }
        else
        {
            definition.ChunkCoords = Vector2I.Zero;
            definition.LocalPosition = world;
        }
    }

    private static bool IsChunked(NpcDefinition definition) =>
        TryLevelContext(out var context, out _) && context.Chunked
        && context.LevelScenePath == definition.SpawnScenePath;

    private static Npc SpawnPreview(NpcDefinition definition, Vector3 world, float rotationY, LevelContext context)
    {
        Node parent = null;
        if (context.Chunked && context.ChunkManager != null)
        {
            var split = PlacementMath.SplitWorld(world.X, world.Y, world.Z);
            parent = context.ChunkManager.GetLoadedChunk(new Vector2I(split.ChunkX, split.ChunkZ));
        }
        parent ??= context.LevelSceneRoot;
        if (parent == null)
        {
            return null;
        }
        var npc = NpcSpawner.Spawn(definition, parent);
        if (npc != null)
        {
            // The definition's LocalPosition is chunk/scene-local; pin the
            // preview to the exact world spot regardless of which parent it got.
            npc.GlobalPosition = world;
            npc.RotationDegrees = new Vector3(0f, rotationY, 0f);
        }
        return npc;
    }

    private static PackedScene ResolveRig(string name, out string error)
    {
        error = null;
        var path = $"{RigDirectory}/{name}.tscn";
        if (!ResourceLoader.Exists(path))
        {
            error = $"Unknown rig '{name}'. Rigs live in {RigDirectory} (e.g. Knight, Barbarian, Mage).";
            return null;
        }
        return GD.Load<PackedScene>(path);
    }

    private static bool TryResolvePosition(IReadOnlyList<string> tokens, out Vector3 position, out string error)
    {
        error = null;
        position = Vector3.Zero;
        if (tokens.Count == 0 || tokens[0] == "here")
        {
            var cursor = DevConsole.PlacementCursorPosition;
            if (cursor == null)
            {
                error = "No cursor target. Enable editor mode and aim at the ground, or pass explicit x y z.";
                return false;
            }
            position = cursor.Value;
            return true;
        }
        if (tokens.Count < 3 || !TryFloat(tokens[0], out var x) || !TryFloat(tokens[1], out var y) || !TryFloat(tokens[2], out var z))
        {
            error = "Expected 'here' or three numeric coordinates: <x> <y> <z>.";
            return false;
        }
        position = new Vector3(x, y, z);
        return true;
    }

    private readonly struct LevelContext
    {
        public LevelContext(string levelScenePath, Node levelSceneRoot, ChunkManager chunkManager)
        {
            LevelScenePath = levelScenePath;
            LevelSceneRoot = levelSceneRoot;
            ChunkManager = chunkManager;
        }

        public string LevelScenePath { get; }
        public Node LevelSceneRoot { get; }
        public ChunkManager ChunkManager { get; }
        public bool Chunked => ChunkManager != null;
    }

    private static bool TryLevelContext(out LevelContext context, out string error)
    {
        context = default;
        error = null;
        var levelRoot = LevelManager.Instance?.LevelRoot;
        var sceneRoot = levelRoot == null ? null : SceneRootOf(levelRoot);
        if (sceneRoot == null || string.IsNullOrEmpty(sceneRoot.SceneFilePath))
        {
            error = "No level is running. Start or load a game first.";
            return false;
        }
        context = new LevelContext(sceneRoot.SceneFilePath, sceneRoot, FindChunkManager(sceneRoot));
        return true;
    }

    private static Node SceneRootOf(Node levelRoot)
    {
        foreach (var child in levelRoot.GetChildren())
        {
            if (!string.IsNullOrEmpty(child.SceneFilePath))
            {
                return child;
            }
        }
        return null;
    }

    private static ChunkManager FindChunkManager(Node node)
    {
        if (node is ChunkManager manager)
        {
            return manager;
        }
        foreach (var child in node.GetChildren())
        {
            if (FindChunkManager(child) is ChunkManager found)
            {
                return found;
            }
        }
        return null;
    }

    private static bool IsPreviewAlive() => preview != null && GodotObject.IsInstanceValid(preview);

    private static void ClearPending()
    {
        if (IsPreviewAlive())
        {
            preview.QueueFree();
        }
        preview = null;
        pending = null;
        pendingWorld = Vector3.Zero;
    }

    private static bool TryFloat(string token, out float value) =>
        float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string Format(Vector3 v) => $"({v.X:0.0}, {v.Y:0.0}, {v.Z:0.0})";

    private static List<string> Sublist(IReadOnlyList<string> source, int start)
    {
        var list = new List<string>();
        for (var i = start; i < source.Count; i++)
        {
            list.Add(source[i]);
        }
        return list;
    }
}
