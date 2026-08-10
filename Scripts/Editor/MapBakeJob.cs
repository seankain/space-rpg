#if TOOLS
using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// The world-map bake, shared by both entry points: the File > Run script
// (MapBaker) and the Project > Tools menu item / auto-rebake plugin
// (MapBakerPlugin). Produces, per area under Scenes/Levels/Chunks/, into
// Resources/Maps/<Area>/: one Chunk_<x>_<z>.png, a landmarks.json, and a
// bake_manifest.json recording each source chunk's content hash so a stale
// map can be caught (see MapBakeManifest and WorldMapBakeFreshnessTests).
// Output is deterministic (fixed camera, light, background), so re-bakes diff
// cleanly and only edited chunks change.
//
// Capture instantiates each chunk under an offscreen SubViewport with an
// orthographic camera looking straight down (image top = world -Z, matching
// MapProjection), then waits a few real drawn frames before reading the
// texture back — a synchronous grab returns black, and chunk floors are
// CSGBox3D whose meshes only cook during an idle step. Chunk scripts are not
// [Tool], so instancing runs no gameplay code; the bake never mutates scenes.
public static class MapBakeJob
{
    public const string ChunksRoot = "res://Scenes/Levels/Chunks";
    private const string MapsRoot = "res://Resources/Maps";

    // Render layer reserved for 3D nodes that must never appear on the map
    // (markers, VFX): put them on this layer only and the bake camera's cull
    // mask skips them.
    public const int MapHiddenRenderLayer = 20;

    // VisualInstance3D layers are 20 bits, all on by default.
    private const uint AllRenderLayers = 0xFFFFF;

    // Well above any chunk structure; Far reaches down past y = 0 with room
    // for below-ground geometry.
    private const float CameraHeight = 100f;

    private static readonly Color SpaceBackground = new(0.05f, 0.06f, 0.09f);

    // Rendered frames to wait per chunk before reading the texture back.
    // Covers CSG mesh cooking (a deferred idle step) plus GPU upload, then a
    // settled frame to capture.
    private const int SettleFrames = 3;

    // Re-bake every area. async because capture waits on real rendered frames;
    // Godot keeps pumping frames while the coroutine is suspended.
    public static async Task RunAsync()
    {
        using var root = DirAccess.Open(ChunksRoot);
        if (root == null)
        {
            GD.PushError($"MapBaker: chunk root '{ChunksRoot}' not found.");
            return;
        }
        await BakeAreasAsync(root.GetDirectories());
    }

    // Re-bake a single area (used by the plugin's auto-rebake on chunk save).
    public static async Task BakeAreaAsync(string areaName) =>
        await BakeAreasAsync(new[] { areaName });

    private static async Task BakeAreasAsync(IEnumerable<string> areaNames)
    {
        var viewport = BuildCaptureRig();
        // The viewport must live in the tree to render; the editor's base
        // control hosts it for the duration of the bake.
        EditorInterface.Singleton.GetBaseControl().AddChild(viewport);
        try
        {
            foreach (var areaName in areaNames)
            {
                await BakeAreaIntoAsync(areaName, viewport);
            }
        }
        finally
        {
            viewport.QueueFree();
        }
        // Pick up the new/updated PNGs so they import as textures right away.
        EditorInterface.Singleton.GetResourceFilesystem().Scan();
        GD.Print("MapBaker: done.");
    }

    private static async Task BakeAreaIntoAsync(string areaName, SubViewport viewport)
    {
        var chunks = ChunkManager.DiscoverChunks($"{ChunksRoot}/{areaName}");
        if (chunks.Count == 0)
        {
            return;
        }
        var areaDir = $"{MapsRoot}/{areaName}";
        DirAccess.MakeDirRecursiveAbsolute(areaDir);
        var landmarks = new MapLandmarksFile();
        var manifest = new MapBakeManifest();
        // Sorted so the landmark manifest's order is stable across bakes.
        foreach (var (coord, scenePath) in chunks.OrderBy(c => c.Key.Y).ThenBy(c => c.Key.X))
        {
            var chunk = GD.Load<PackedScene>(scenePath).Instantiate<Node3D>();
            viewport.AddChild(chunk);
            // Wait for real drawn frames before reading back: the first lets
            // CSG floors cook and meshes upload, the rest settle the image.
            // Reading immediately (or after ForceDraw) yields a black texture.
            for (var frame = 0; frame < SettleFrames; frame++)
            {
                await viewport.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
            var image = viewport.GetTexture().GetImage();
            var pngPath = $"{areaDir}/Chunk_{coord.X}_{coord.Y}.png";
            var error = image.SavePng(pngPath);
            if (error != Error.Ok)
            {
                GD.PushError($"MapBaker: failed to save '{pngPath}': {error}");
            }
            else
            {
                GD.Print($"MapBaker: baked {pngPath}");
            }
            CollectLandmarks(chunk, chunk, coord, landmarks);
            manifest.ChunkHashes[scenePath.GetFile()] = HashChunkScene(scenePath);
            viewport.RemoveChild(chunk);
            chunk.Free();
        }
        WriteText($"{areaDir}/landmarks.json", landmarks.ToJson(),
            $"landmarks.json ({landmarks.Landmarks.Count} landmarks)");
        WriteText($"{areaDir}/bake_manifest.json", manifest.ToJson(),
            $"bake_manifest.json ({manifest.ChunkHashes.Count} chunks)");
    }

    private static string HashChunkScene(string scenePath)
    {
        using var file = FileAccess.Open(scenePath, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PushWarning($"MapBaker: could not hash '{scenePath}': {FileAccess.GetOpenError()}");
            return "";
        }
        return MapBakeManifest.HashSceneText(file.GetAsText());
    }

    private static void WriteText(string path, string contents, string label)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (file == null)
        {
            GD.PushError($"MapBaker: failed to write '{path}': {FileAccess.GetOpenError()}");
            return;
        }
        file.StoreString(contents);
        GD.Print($"MapBaker: baked {path.GetFile()} — {label}");
    }

    // Scene-authored map data only: portals, entrance doors, and world pickups.
    // Stores are NPC data (ShopkeeperRole) and the map UI reads those live from
    // NpcDatabase — see docs/plans/world-map.md.
    private static void CollectLandmarks(Node3D chunk, Node node, Vector2I coord, MapLandmarksFile into)
    {
        switch (node)
        {
            case Portal portal:
                into.Landmarks.Add(MakeLandmark(
                    MapLandmark.PortalType, portal.TargetDisplayName, chunk, portal, coord,
                    targetScenePath: portal.TargetScenePath));
                break;
            // Exit doors (ReturnsToPrevious) belong to interiors, not chunks;
            // skip them if one ever appears.
            case Door { ReturnsToPrevious: false } door:
                into.Landmarks.Add(MakeLandmark(
                    MapLandmark.DoorType, door.TargetDisplayName, chunk, door, coord,
                    targetScenePath: door.TargetScenePath));
                break;
            // Not drawn on the map: recorded so a quest marker can point at an
            // item still lying in a chunk that isn't streamed in
            // (quest-markers.md Phase 2).
            case Pickup pickup:
                into.Landmarks.Add(MakeLandmark(
                    MapLandmark.PickupType, ItemCatalog.Get(pickup.ItemId)?.Name ?? "", chunk, pickup, coord,
                    itemId: pickup.ItemId));
                break;
        }
        foreach (var child in node.GetChildren())
        {
            CollectLandmarks(chunk, child, coord, into);
        }
    }

    private static MapLandmark MakeLandmark(
        string type, string name, Node3D chunk, Node3D node, Vector2I coord,
        uint itemId = 0, string targetScenePath = null)
    {
        // The chunk sits at the viewport origin during capture, so positions
        // relative to it are chunk-local — exactly what MapProjection expects.
        var local = chunk.ToLocal(node.GlobalPosition);
        return new MapLandmark
        {
            Type = type,
            Name = name,
            X = MapProjection.ChunkToWorldX(coord.X, local.X),
            Z = MapProjection.ChunkToWorldZ(coord.Y, local.Z),
            ItemId = itemId,
            TargetScenePath = targetScenePath ?? "",
        };
    }

    private static SubViewport BuildCaptureRig()
    {
        var viewport = new SubViewport
        {
            Name = "MapBakeViewport",
            Size = new Vector2I(MapProjection.ChunkPixels, MapProjection.ChunkPixels),
            OwnWorld3D = true,
            // No MSAA: on the Compatibility renderer, reading back a
            // multisampled 3D target is a known source of black images.
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        var camera = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal,
            // Ortho size is vertical extent; the viewport is square, so one
            // chunk fills the frame exactly.
            Size = MapProjection.ChunkSize,
            Position = new Vector3(0f, CameraHeight, 0f),
            // Straight down, with camera-up at world -Z: image top is north,
            // matching MapProjection's orientation contract.
            RotationDegrees = new Vector3(-90f, 0f, 0f),
            Near = 0.5f,
            Far = CameraHeight * 2f,
            CullMask = AllRenderLayers & ~(1u << (MapHiddenRenderLayer - 1)),
            Current = true,
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = SpaceBackground,
                // Flat ambient fill so faces the sun misses stay readable.
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = Colors.White,
                AmbientLightEnergy = 0.4f,
            },
        };
        viewport.AddChild(camera);
        viewport.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-55f, -30f, 0f),
            LightEnergy = 1.1f,
        });
        return viewport;
    }
}
#endif
