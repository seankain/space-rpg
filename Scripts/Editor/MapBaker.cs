#if TOOLS
using Godot;
using System.Linq;

// Bakes the world map: one top-down PNG per level chunk, plus a landmarks
// manifest per area, into Resources/Maps/<AreaName>/. Re-run it whenever
// chunk scenes change; output is deterministic (fixed camera, light, and
// background), so re-bakes diff cleanly and only edited chunks change.
//
// How to run: open this script in the Godot script editor and use
// File > Run (Ctrl+Shift+X). A one-click Project > Tools menu item is
// planned for a later phase (see docs/plans/world-map.md).
//
// Capture works by instantiating each Chunk_<x>_<z>.tscn under an offscreen
// SubViewport with its own world, an orthographic camera looking straight
// down (image top = world -Z, matching MapProjection), and forcing the
// renderer to draw. Chunk scripts are not [Tool], so instancing here runs no
// gameplay code — the bake never mutates chunk scenes.
[Tool]
public partial class MapBaker : EditorScript
{
    private const string ChunksRoot = "res://Scenes/Levels/Chunks";
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

    public override void _Run()
    {
        using var root = DirAccess.Open(ChunksRoot);
        if (root == null)
        {
            GD.PushError($"MapBaker: chunk root '{ChunksRoot}' not found.");
            return;
        }
        var viewport = BuildCaptureRig();
        // The viewport must live in the tree to render; the editor's base
        // control hosts it for the duration of the bake.
        EditorInterface.Singleton.GetBaseControl().AddChild(viewport);
        try
        {
            foreach (var areaName in root.GetDirectories())
            {
                BakeArea(areaName, viewport);
            }
        }
        finally
        {
            viewport.QueueFree();
        }
        // Pick up the new/updated PNGs so they import as textures right away.
        EditorInterface.Singleton.GetResourceFilesystem().Scan();
    }

    private static void BakeArea(string areaName, SubViewport viewport)
    {
        var chunks = ChunkManager.DiscoverChunks($"{ChunksRoot}/{areaName}");
        if (chunks.Count == 0)
        {
            return;
        }
        var areaDir = $"{MapsRoot}/{areaName}";
        DirAccess.MakeDirRecursiveAbsolute(areaDir);
        var landmarks = new MapLandmarksFile();
        // Sorted so the landmark manifest's order is stable across bakes.
        foreach (var (coord, scenePath) in chunks.OrderBy(c => c.Key.Y).ThenBy(c => c.Key.X))
        {
            var chunk = GD.Load<PackedScene>(scenePath).Instantiate<Node3D>();
            viewport.AddChild(chunk);
            // Two forced draws: the first uploads freshly instanced meshes,
            // the second is the settled frame we capture.
            RenderingServer.ForceDraw();
            RenderingServer.ForceDraw();
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
            viewport.RemoveChild(chunk);
            chunk.Free();
        }
        using var file = FileAccess.Open($"{areaDir}/landmarks.json", FileAccess.ModeFlags.Write);
        if (file == null)
        {
            GD.PushError($"MapBaker: failed to write '{areaDir}/landmarks.json': {FileAccess.GetOpenError()}");
            return;
        }
        file.StoreString(landmarks.ToJson());
        GD.Print($"MapBaker: baked {areaDir}/landmarks.json ({landmarks.Landmarks.Count} landmarks)");
    }

    // Scene-authored landmarks only: portals and entrance doors. Stores are
    // NPC data (ShopkeeperRole) and the map UI reads those live from
    // NpcDatabase — see docs/plans/world-map.md.
    private static void CollectLandmarks(Node3D chunk, Node node, Vector2I coord, MapLandmarksFile into)
    {
        switch (node)
        {
            case Portal portal:
                into.Landmarks.Add(MakeLandmark(MapLandmark.PortalType, portal.TargetDisplayName, chunk, portal, coord));
                break;
            // Exit doors (ReturnsToPrevious) belong to interiors, not chunks;
            // skip them if one ever appears.
            case Door { ReturnsToPrevious: false } door:
                into.Landmarks.Add(MakeLandmark(MapLandmark.DoorType, door.TargetDisplayName, chunk, door, coord));
                break;
        }
        foreach (var child in node.GetChildren())
        {
            CollectLandmarks(chunk, child, coord, into);
        }
    }

    private static MapLandmark MakeLandmark(string type, string name, Node3D chunk, Node3D node, Vector2I coord)
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
        };
    }

    private static SubViewport BuildCaptureRig()
    {
        var viewport = new SubViewport
        {
            Name = "MapBakeViewport",
            Size = new Vector2I(MapProjection.ChunkPixels, MapProjection.ChunkPixels),
            OwnWorld3D = true,
            Msaa3D = Viewport.Msaa.Msaa4X,
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
