using Godot;

// A single landmark glyph on the world map: a portal, a door, or a store.
// Drawn in code rather than from a texture (same choice as the player arrow
// in MapMenu) so there are no binary assets to bake or theme, and being a
// Control it gets hover tooltips for free. MapMenu sizes and positions it and
// counter-scales it against the map zoom so it stays a constant on-screen
// size; this class only draws the glyph and reports its hover name.
public partial class MapLandmarkIcon : Control
{
    public enum LandmarkKind { Portal, Door, Store }

    // The icon's on-map footprint, in unscaled map pixels. MapMenu offsets by
    // half of this to center the glyph on the landmark's position.
    public const int Diameter = 24;

    // Set before AddChild; read at draw time.
    public LandmarkKind Kind { get; set; } = LandmarkKind.Portal;

    private static readonly Color Backing = new(0f, 0f, 0f, 0.45f);
    private static readonly Color PortalRing = new(0.72f, 0.45f, 1f);
    private static readonly Color PortalCore = new(0.85f, 0.95f, 1f);
    private static readonly Color DoorColor = new(0.35f, 0.85f, 0.5f);
    private static readonly Color DoorOpening = new(0.09f, 0.18f, 0.12f);
    private static readonly Color StoreColor = new(1f, 0.75f, 0.25f);

    public static LandmarkKind KindFromType(string landmarkType) => landmarkType switch
    {
        MapLandmark.DoorType => LandmarkKind.Door,
        _ => LandmarkKind.Portal,
    };

    public override void _Ready()
    {
        Size = new Vector2(Diameter, Diameter);
        PivotOffset = new Vector2(Diameter / 2f, Diameter / 2f);
        // Pass, not Stop, so hovering shows the tooltip but pan/zoom events
        // still reach the map window underneath.
        MouseFilter = MouseFilterEnum.Pass;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var c = new Vector2(Diameter / 2f, Diameter / 2f);
        // A dark disc behind every glyph so icons stay legible over bright
        // map areas.
        DrawCircle(c, 11f, Backing);

        switch (Kind)
        {
            case LandmarkKind.Portal:
                DrawArc(c, 8f, 0f, Mathf.Tau, 32, PortalRing, 3f, true);
                DrawCircle(c, 3f, PortalCore);
                break;
            case LandmarkKind.Door:
                DrawRect(new Rect2(c.X - 5f, c.Y - 7f, 10f, 14f), DoorColor);
                DrawRect(new Rect2(c.X - 3f, c.Y - 5f, 6f, 12f), DoorOpening);
                DrawCircle(new Vector2(c.X + 1.5f, c.Y + 1f), 1.2f, DoorColor);
                break;
            case LandmarkKind.Store:
                DrawColoredPolygon(
                    new[]
                    {
                        new Vector2(c.X - 6f, c.Y - 2f),
                        new Vector2(c.X + 6f, c.Y - 2f),
                        new Vector2(c.X + 5f, c.Y + 7f),
                        new Vector2(c.X - 5f, c.Y + 7f),
                    },
                    StoreColor);
                DrawArc(new Vector2(c.X, c.Y - 2f), 4f, Mathf.Pi, Mathf.Tau, 12, StoreColor, 2f, true);
                break;
        }
    }
}
