using Godot;

// A tracked quest's objective on the world map (quest-markers.md Phase 4).
// Drawn in code like its neighbours (MapLandmarkIcon, the player arrow): no
// asset to theme, and being a Control it gets the objective text as a hover
// tooltip for free. MapMenu positions it and counter-scales it against the map
// zoom; this class only draws.
//
// A diamond rather than another disc, so an objective reads as different in
// kind from the landmarks underneath it — gold for a main quest, pale blue for
// a side quest, and hollow when the marker stands on a door leading to where
// the target really is rather than on the target itself.
public partial class QuestMarkerIcon : Control
{
    // On-map footprint in unscaled map pixels; MapMenu offsets by half of it to
    // centre the glyph. Larger than a landmark's 24 — the objective should win
    // the eye when it sits on top of one.
    public const int Diameter = 28;

    // Set before AddChild; read at draw time.
    public bool SideQuest { get; set; }

    // True when this marks the way *to* the target (an entrance) rather than
    // the target itself.
    public bool IsEntrance { get; set; }

    private static readonly Color Backing = new(0f, 0f, 0f, 0.55f);
    private static readonly Color MainColor = new(1f, 0.82f, 0.25f);
    private static readonly Color SideColor = new(0.55f, 0.8f, 1f);
    private static readonly Color Outline = new(0.05f, 0.05f, 0.08f);

    public override void _Ready()
    {
        Size = new Vector2(Diameter, Diameter);
        PivotOffset = new Vector2(Diameter / 2f, Diameter / 2f);
        // Pass, not Stop: hovering shows the objective, but drag and wheel
        // still reach the map underneath.
        MouseFilter = MouseFilterEnum.Pass;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var c = new Vector2(Diameter / 2f, Diameter / 2f);
        var color = SideQuest ? SideColor : MainColor;
        DrawCircle(c, 12f, Backing);

        var diamond = new[]
        {
            new Vector2(c.X, c.Y - 10f),
            new Vector2(c.X + 7f, c.Y),
            new Vector2(c.X, c.Y + 10f),
            new Vector2(c.X - 7f, c.Y),
        };
        if (IsEntrance)
        {
            // Hollow: the objective is through here, not here.
            DrawPolyline(new[] { diamond[0], diamond[1], diamond[2], diamond[3], diamond[0] }, color, 2.5f, true);
        }
        else
        {
            DrawColoredPolygon(diamond, color);
            DrawPolyline(new[] { diamond[0], diamond[1], diamond[2], diamond[3], diamond[0] }, Outline, 1.5f, true);
        }
    }
}
