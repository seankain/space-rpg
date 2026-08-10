using Godot;
using System.Collections.Generic;
using System.Linq;

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
// Landmark icons hang on the same world layer, and so do the tracked quest's
// objectives (quest-markers.md Phase 4) — with the objective text in a line
// beside the area name, so the map answers "what am I doing" by itself.
public partial class MapMenu : Control
{
    // Same root the landmark manifests load from, so the images and the data
    // baked beside them can't drift apart.
    private const string MapsRoot = MapLandmarkCatalog.MapsRoot;

    private const float MinZoom = 0.25f;
    private const float MaxZoom = 4f;
    private const float ZoomStep = 1.15f;

    // The clipped window the map is viewed through; its child `world` is the
    // pannable/zoomable layer holding chunk tiles and the marker.
    private Control clip;
    private Control world;
    private Label header;
    private Label objectiveLabel;
    private Label fallback;
    private Polygon2D playerMarker;

    // Landmark and quest-marker icons on the world layer, kept so their
    // counter-scale can track the zoom (they stay a constant on-screen size).
    private readonly List<Control> mapIcons = new();

    // Where the tracked quest's drawn objectives are, in map pixels — what
    // FocusTrackedObjective frames.
    private readonly List<Vector2> questMarkerPixels = new();

    private float zoom = 1f;
    private bool dragging;

    // A "Show on Map" that landed before the map was built (see
    // FocusTrackedObjective).
    private bool focusObjectivePending;

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
        // Switching tabs already re-runs Refresh; this covers tracking that
        // moves while the map is up (the console's quest track, or a quest
        // completing).
        QuestTracking.Changed += OnTrackedQuestChanged;
    }

    public override void _ExitTree() => QuestTracking.Changed -= OnTrackedQuestChanged;

    private void OnTrackedQuestChanged(uint questId)
    {
        if (IsVisibleInTree())
        {
            Refresh();
        }
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

        // The tracked quest's objectives, so the map answers "what am I doing"
        // without a trip back to the Quests tab. Right-aligned in the strip
        // beside the area name, above the map window.
        objectiveLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            ClipText = true,
            Visible = false,
        };
        objectiveLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
        objectiveLabel.OffsetLeft = -640;
        objectiveLabel.OffsetTop = 8;
        objectiveLabel.OffsetRight = -12;
        objectiveLabel.OffsetBottom = 40;
        objectiveLabel.GrowHorizontal = GrowDirection.Begin;
        objectiveLabel.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(objectiveLabel);

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
        mapIcons.Clear();
        questMarkerPixels.Clear();
        objectiveLabel.Visible = false;
        hasMap = false;

        var chunkManager = ChunkManager.FindIn(LevelManager.Instance?.LevelRoot);
        if (chunkManager == null)
        {
            fallback.Visible = true;
            header.Text = SaveManager.Instance?.CurrentState?.LocationName ?? "Map";
            return;
        }

        var areaName = chunkManager.AreaName;
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
        BuildLandmarks(areaName, grid.Keys, chunkManager);
        // After the landmarks so an objective sits above the door or store it
        // shares a spot with, and before the player arrow, which stays on top.
        BuildQuestMarkers(areaName, chunkManager);
        BuildPlayerMarker();

        fallback.Visible = false;
        header.Text = SaveManager.Instance?.CurrentState?.LocationName ?? areaName;
        hasMap = true;
        zoom = 1f;
        ApplyZoom(1f, clip.Size / 2f);
        CenterOnPlayer();
        if (focusObjectivePending)
        {
            focusObjectivePending = false;
            FocusTrackedObjective();
        }
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

    // Two landmark sources, by design (see docs/plans/world-map.md): portals
    // and doors are scene-authored and baked into landmarks.json; stores are
    // NPC data, read live from NpcDatabase so moving a shopkeeper's .tres
    // never leaves a stale manifest.
    private void BuildLandmarks(string areaName, IEnumerable<Vector2I> coords, ChunkManager chunkManager)
    {
        AddBakedLandmarks(areaName);
        AddShopkeeperLandmarks(coords, chunkManager.LevelScenePath());
    }

    private void AddBakedLandmarks(string areaName)
    {
        // A missing manifest (un-baked area) reads as empty. Not everything in
        // the file is drawn: pickups are recorded there for quest markers to
        // find, and would mean nothing to a player as a generic icon.
        foreach (var landmark in MapLandmarkCatalog.ForArea(areaName).Landmarks)
        {
            if (landmark.Type is not (MapLandmark.PortalType or MapLandmark.DoorType))
            {
                continue;
            }
            AddLandmarkIcon(MapLandmarkIcon.KindFromType(landmark.Type), landmark.Name, landmark.X, landmark.Z);
        }
    }

    private void AddShopkeeperLandmarks(IEnumerable<Vector2I> coords, string levelScenePath)
    {
        if (string.IsNullOrEmpty(levelScenePath))
        {
            return;
        }
        foreach (var coord in coords)
        {
            foreach (var npc in NpcDatabase.ForChunk(levelScenePath, coord))
            {
                if (!HasShopkeeperRole(npc))
                {
                    continue;
                }
                var worldX = MapProjection.ChunkToWorldX(npc.ChunkCoords.X, npc.LocalPosition.X);
                var worldZ = MapProjection.ChunkToWorldZ(npc.ChunkCoords.Y, npc.LocalPosition.Z);
                AddLandmarkIcon(MapLandmarkIcon.LandmarkKind.Store, npc.DisplayName, worldX, worldZ);
            }
        }
    }

    private static bool HasShopkeeperRole(NpcDefinition npc)
    {
        foreach (var role in npc.Roles)
        {
            if (role is ShopkeeperRole)
            {
                return true;
            }
        }
        return false;
    }

    // The tracked quest's objectives (quest-markers.md Phase 4). Which markers
    // apply and where they belong on *this* map is decided by the engine-free
    // QuestMarkerPlacements; this only draws the answer and reports the rest in
    // the objective line.
    private void BuildQuestMarkers(string areaName, ChunkManager chunkManager)
    {
        var state = SaveManager.Instance?.CurrentState;
        if (state == null)
        {
            return;
        }
        if (state.TrackedQuestId == 0)
        {
            // Silent when there is nothing to follow, but a player who has
            // untracked their quests shouldn't be left wondering where the
            // markers went.
            if (state.Quests.Any(progress => progress.State == QUESTSUCCESSSTATE.InProgress))
            {
                objectiveLabel.Text = "No quest tracked — pick one in the Quests tab";
                objectiveLabel.Visible = true;
            }
            return;
        }
        var levelScenePath = chunkManager.LevelScenePath();
        var placements = QuestMarkerPlacements.ForTrackedQuest(
            state,
            new QuestTargetLocator(GetTree(), areaName, levelScenePath),
            areaName,
            levelScenePath,
            MapLandmarkCatalog.ForArea(areaName),
            GD.PushWarning);
        if (placements.Count == 0)
        {
            return;
        }
        foreach (var placement in placements)
        {
            if (placement.IsDrawn)
            {
                AddQuestMarkerIcon(placement);
            }
        }
        var quest = QuestCatalog.Get(state.TrackedQuestId);
        objectiveLabel.Text = $"{quest?.Title}: "
            + string.Join("  •  ", placements.Select(placement => placement.DisplayText));
        objectiveLabel.Visible = true;
    }

    private void AddQuestMarkerIcon(QuestMarkerPlacement placement)
    {
        const float half = QuestMarkerIcon.Diameter / 2f;
        var pixel = new Vector2(
            MapProjection.WorldToPixelX(placement.WorldX, minChunkX),
            MapProjection.WorldToPixelY(placement.WorldZ, minChunkZ));
        var icon = new QuestMarkerIcon
        {
            SideQuest = placement.SideQuest,
            IsEntrance = placement.Kind == QuestMarkerPlacementKind.AtEntrance,
            Position = pixel - new Vector2(half, half),
            Scale = Vector2.One / zoom,
            TooltipText = placement.DisplayText,
        };
        world.AddChild(icon);
        mapIcons.Add(icon);
        questMarkerPixels.Add(pixel);
    }

    // Frames the objective nearest the player instead of the player — what the
    // Quests tab's "Show on Map" asks for. Falls back to the player when the
    // tracked quest has nothing drawn on this map.
    public void FocusTrackedObjective()
    {
        if (!hasMap)
        {
            // Asked before this tab has built itself: hold the request rather
            // than dropping it, and honour it at the end of the next Refresh.
            focusObjectivePending = true;
            return;
        }
        if (questMarkerPixels.Count == 0)
        {
            CenterOnPlayer();
            return;
        }
        var from = PlayerPixel() ?? questMarkerPixels[0];
        var nearest = questMarkerPixels[0];
        foreach (var pixel in questMarkerPixels)
        {
            if (pixel.DistanceSquaredTo(from) < nearest.DistanceSquaredTo(from))
            {
                nearest = pixel;
            }
        }
        world.Position = clip.Size / 2f - nearest * zoom;
    }

    private void AddLandmarkIcon(MapLandmarkIcon.LandmarkKind kind, string name, float worldX, float worldZ)
    {
        const float half = MapLandmarkIcon.Diameter / 2f;
        var pixel = new Vector2(
            MapProjection.WorldToPixelX(worldX, minChunkX),
            MapProjection.WorldToPixelY(worldZ, minChunkZ));
        var icon = new MapLandmarkIcon
        {
            Kind = kind,
            // Position places the top-left; the icon's pivot is its center, so
            // the glyph and its counter-scale stay centered on `pixel`.
            Position = pixel - new Vector2(half, half),
            Scale = Vector2.One / zoom,
            TooltipText = name,
        };
        world.AddChild(icon);
        mapIcons.Add(icon);
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
        // No player in the scene (e.g. viewing between transitions): frame the
        // whole area instead.
        world.Position = clip.Size / 2f - (PlayerPixel() ?? mapPixelSize / 2f) * zoom;
    }

    // The player's position in map pixels, or null when there is no player in
    // the scene.
    private Vector2? PlayerPixel()
    {
        if (GetTree().GetFirstNodeInGroup(Player.GroupName) is not Player player)
        {
            return null;
        }
        var pos = player.GlobalPosition;
        return new Vector2(
            MapProjection.WorldToPixelX(pos.X, minChunkX),
            MapProjection.WorldToPixelY(pos.Z, minChunkZ));
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
        // Landmarks live on the scaled world layer; counter-scale them so they
        // keep a constant on-screen size (the player marker does the same in
        // UpdatePlayerMarker).
        foreach (var icon in mapIcons)
        {
            icon.Scale = Vector2.One / zoom;
        }
    }

}
