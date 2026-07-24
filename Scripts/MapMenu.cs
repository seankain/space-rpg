using Godot;
using System.Collections.Generic;

// The Map tab of the in-game menu: a top-down map of the current area,
// stitched from the per-chunk PNGs the editor bake tool wrote to
// Resources/Maps/<Area>/ (see docs/plans/world-map.md and MapBaker). The grid
// is the same one ChunkManager streams — resolved live from the running
// level's ChunkManager — so the map and the world can never disagree about
// where a chunk is. A pannable, zoomable layer holds one TextureRect per
// chunk plus a live player marker; interiors and unchunked scenes, which have
// no ChunkManager, show a "no map" message instead of erroring.
//
// The tab's children are built in code (like Npc/Door/Portal build their bits
// in code) because the Map tab is an otherwise empty Control in the scene.
// Phase 3 hangs landmark icons on the same world layer.
public partial class MapMenu : Control
{
    private const string MapsRoot = "res://Resources/Maps";

    private const float MinZoom = 0.25f;
    private const float MaxZoom = 4f;
    private const float ZoomStep = 1.15f;

    // The clipped window the map is viewed through; its child `world` is the
    // pannable/zoomable layer holding chunk tiles and the marker.
    private Control clip;
    private Control world;
    private Label header;
    private Label fallback;
    private Polygon2D playerMarker;

    private float zoom = 1f;
    private bool dragging;

    // Grid origin of the current map, so world positions project onto the
    // stitched image the same way MapBaker laid it out.
    private int minChunkX;
    private int minChunkZ;
    private Vector2 mapPixelSize;
    private bool hasMap;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        BuildUi();
        VisibilityChanged += () =>
        {
            if (IsVisibleInTree())
            {
                Refresh();
            }
        };
    }

    public override void _Process(double delta)
    {
        if (hasMap && IsVisibleInTree())
        {
            UpdatePlayerMarker();
        }
    }

    private void BuildUi()
    {
        header = new Label
        {
            Text = "Map",
            OffsetLeft = 12,
            OffsetTop = 8,
        };
        header.AddThemeFontSizeOverride("font_size", 24);
        AddChild(header);

        clip = new Control
        {
            ClipContents = true,
            MouseFilter = MouseFilterEnum.Stop,
        };
        clip.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        clip.OffsetTop = 44;
        clip.GuiInput += OnMapGuiInput;
        AddChild(clip);

        // A dark backdrop so seams and un-baked cells read as space, not gaps.
        var backdrop = new ColorRect { Color = new Color(0.03f, 0.04f, 0.06f) };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        backdrop.MouseFilter = MouseFilterEnum.Ignore;
        clip.AddChild(backdrop);

        world = new Control { MouseFilter = MouseFilterEnum.Ignore };
        clip.AddChild(world);

        fallback = new Label
        {
            Text = "No map available for this area.",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visible = false,
        };
        fallback.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        fallback.MouseFilter = MouseFilterEnum.Ignore;
        clip.AddChild(fallback);

        BuildZoomControls();
    }

    private void BuildZoomControls()
    {
        var buttons = new HBoxContainer();
        buttons.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
        buttons.OffsetLeft = -160;
        buttons.OffsetTop = -44;
        buttons.OffsetRight = -12;
        buttons.OffsetBottom = -12;
        buttons.GrowHorizontal = GrowDirection.Begin;
        buttons.GrowVertical = GrowDirection.Begin;

        var zoomOut = new Button { Text = "−", CustomMinimumSize = new Vector2(40, 0) };
        zoomOut.Pressed += () => ApplyZoom(zoom / ZoomStep, clip.Size / 2f);
        var recenter = new Button { Text = "Recenter" };
        recenter.Pressed += CenterOnPlayer;
        var zoomIn = new Button { Text = "+", CustomMinimumSize = new Vector2(40, 0) };
        zoomIn.Pressed += () => ApplyZoom(zoom * ZoomStep, clip.Size / 2f);

        buttons.AddChild(zoomOut);
        buttons.AddChild(recenter);
        buttons.AddChild(zoomIn);
        clip.AddChild(buttons);
    }

    // Rebuilds the map for whatever area is running. Cheap enough to run every
    // time the tab is shown, which also picks up a fresh bake without a
    // restart.
    private void Refresh()
    {
        foreach (var child in world.GetChildren())
        {
            child.QueueFree();
        }
        playerMarker = null;
        hasMap = false;

        var chunkManager = FindChunkManager(LevelManager.Instance?.LevelRoot);
        if (chunkManager == null)
        {
            fallback.Visible = true;
            header.Text = SaveManager.Instance?.CurrentState?.LocationName ?? "Map";
            return;
        }

        var areaName = chunkManager.ChunkDirectory.GetFile(); // basename of the dir
        var grid = ChunkManager.DiscoverChunks(chunkManager.ChunkDirectory);
        if (grid.Count == 0)
        {
            fallback.Visible = true;
            header.Text = SaveManager.Instance?.CurrentState?.LocationName ?? areaName;
            return;
        }

        minChunkX = int.MaxValue;
        minChunkZ = int.MaxValue;
        var maxChunkX = int.MinValue;
        var maxChunkZ = int.MinValue;
        foreach (var coord in grid.Keys)
        {
            minChunkX = Mathf.Min(minChunkX, coord.X);
            minChunkZ = Mathf.Min(minChunkZ, coord.Y);
            maxChunkX = Mathf.Max(maxChunkX, coord.X);
            maxChunkZ = Mathf.Max(maxChunkZ, coord.Y);
        }
        mapPixelSize = new Vector2(
            (maxChunkX - minChunkX + 1) * MapProjection.ChunkPixels,
            (maxChunkZ - minChunkZ + 1) * MapProjection.ChunkPixels);

        BuildChunkTiles(grid.Keys, areaName);
        BuildPlayerMarker();

        fallback.Visible = false;
        header.Text = SaveManager.Instance?.CurrentState?.LocationName ?? areaName;
        hasMap = true;
        zoom = 1f;
        ApplyZoom(1f, clip.Size / 2f);
        CenterOnPlayer();
    }

    private void BuildChunkTiles(IEnumerable<Vector2I> coords, string areaName)
    {
        foreach (var coord in coords)
        {
            var imagePath = $"{MapsRoot}/{areaName}/Chunk_{coord.X}_{coord.Y}.png";
            if (!ResourceLoader.Exists(imagePath))
            {
                // No baked image yet: leave the cell empty rather than error.
                continue;
            }
            var tile = new TextureRect
            {
                Texture = GD.Load<Texture2D>(imagePath),
                // Explicit size: a standalone TextureRect (no container) keeps
                // size (0,0) otherwise and its Scale stretch draws nothing.
                Size = new Vector2(MapProjection.ChunkPixels, MapProjection.ChunkPixels),
                Position = new Vector2(
                    (coord.X - minChunkX) * MapProjection.ChunkPixels,
                    (coord.Y - minChunkZ) * MapProjection.ChunkPixels),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            world.AddChild(tile);
        }
    }

    private void BuildPlayerMarker()
    {
        // Drawn, not textured: a small triangle pointing up (map north = world
        // -Z). Counter-scaled against zoom in UpdatePlayerMarker so it stays a
        // constant on-screen size. Added last so it sits above the tiles.
        playerMarker = new Polygon2D
        {
            Polygon = new[] { new Vector2(0, -9), new Vector2(6, 7), new Vector2(-6, 7) },
            Color = new Color(0.3f, 0.8f, 1f),
        };
        world.AddChild(playerMarker);
    }

    private void UpdatePlayerMarker()
    {
        if (playerMarker == null
            || GetTree().GetFirstNodeInGroup(Player.GroupName) is not Player player)
        {
            return;
        }
        var pos = player.GlobalPosition;
        playerMarker.Position = new Vector2(
            MapProjection.WorldToPixelX(pos.X, minChunkX),
            MapProjection.WorldToPixelY(pos.Z, minChunkZ));
        playerMarker.Scale = Vector2.One / zoom;

        // The player's facing is meshRoot's yaw, where yaw 0 aims at world +Z
        // (south, toward the bottom of the map). Rotate the north-pointing
        // arrow so its tip follows that facing in screen space (x right, y
        // down): the up arrow (0,-1) rotates to (sin r, -cos r), which points
        // along facing (sin yaw, cos yaw) when r = π − yaw.
        var yaw = player.meshRoot?.Rotation.Y ?? 0f;
        playerMarker.Rotation = Mathf.Pi - yaw;
    }

    private void CenterOnPlayer()
    {
        if (!hasMap)
        {
            return;
        }
        Vector2 focus;
        if (GetTree().GetFirstNodeInGroup(Player.GroupName) is Player player)
        {
            var pos = player.GlobalPosition;
            focus = new Vector2(
                MapProjection.WorldToPixelX(pos.X, minChunkX),
                MapProjection.WorldToPixelY(pos.Z, minChunkZ));
        }
        else
        {
            // No player in the scene (e.g. viewing between transitions): frame
            // the whole area instead.
            focus = mapPixelSize / 2f;
        }
        world.Position = clip.Size / 2f - focus * zoom;
    }

    private void OnMapGuiInput(InputEvent @event)
    {
        if (!hasMap)
        {
            return;
        }
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true } wheelUp:
                ApplyZoom(zoom * ZoomStep, wheelUp.Position);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true } wheelDown:
                ApplyZoom(zoom / ZoomStep, wheelDown.Position);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } click:
                dragging = click.Pressed;
                break;
            case InputEventMouseMotion motion when dragging:
                world.Position += motion.Relative;
                break;
        }
    }

    // Zooms toward `pivot` (a point in clip-local pixels) so whatever is under
    // the cursor stays put.
    private void ApplyZoom(float target, Vector2 pivot)
    {
        var newZoom = Mathf.Clamp(target, MinZoom, MaxZoom);
        var localBefore = (pivot - world.Position) / zoom;
        zoom = newZoom;
        world.Scale = new Vector2(zoom, zoom);
        world.Position = pivot - localBefore * zoom;
    }

    private static ChunkManager FindChunkManager(Node node)
    {
        if (node == null)
        {
            return null;
        }
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
}
