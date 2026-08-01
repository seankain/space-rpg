using Godot;

// The editor-mode pointer probe. While editor mode is active DevConsole
// parents one of these under the scene root; each physics frame it casts a ray
// from the active camera through the **mouse position** into world geometry
// and parks a translucent marker at the hit point.
//
// It followed the screen centre before the free-camera rework, back when the
// pointer was captured for look. Editor mode is point-and-click now — the
// author steers on held right-mouse and otherwise has a free cursor — so the
// probe follows that cursor. The hit position is what the `here` token
// resolves to, what the editor HUD reports, and where the Place NPC tool drops
// a character; the hit collider is how the Edit NPC tool knows which NPC was
// clicked.
public partial class EditorCursor : Node3D
{
    private const float RayLength = 1000f;

    private MeshInstance3D marker;

    // The current world hit point, or null when the ray misses (or there is no
    // camera). DevConsole reads this for the HUD and the `here` token.
    public Vector3? WorldPosition { get; private set; }

    // The body the ray landed on, or null. The Edit NPC tool walks up from
    // this to the owning Npc node.
    public GodotObject Collider { get; private set; }

    // The Npc under the pointer, or null — the collider is the NPC's own
    // CharacterBody3D, so no ancestor walk is needed, but rigs and props
    // parented under one would arrive as children.
    public Npc HoveredNpc => FindNpc(Collider as Node);

    public override void _Ready()
    {
        marker = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.35f, Height = 0.7f },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.3f, 0.9f, 1f, 0.45f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                // Read as a target overlay, not a lit object in the scene.
                NoDepthTest = true,
            },
            Visible = false,
        };
        AddChild(marker);
    }

    public override void _PhysicsProcess(double delta)
    {
        var viewport = GetViewport();
        var camera = viewport?.GetCamera3D();
        if (camera == null)
        {
            SetHit(null, null);
            return;
        }
        // While the pointer is captured for a look-turn its position is frozen
        // at the grab point, so aim down the view axis instead — that keeps the
        // marker meaningful mid-turn rather than pinned to a stale pixel.
        var screenPoint = Input.MouseMode == Input.MouseModeEnum.Captured
            ? viewport.GetVisibleRect().Size * 0.5f
            : viewport.GetMousePosition();
        var from = camera.ProjectRayOrigin(screenPoint);
        var to = from + camera.ProjectRayNormal(screenPoint) * RayLength;
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        // NPCs are CharacterBody3Ds; without this the probe only ever sees the
        // ground and the Edit tool would have nothing to click.
        query.CollideWithAreas = false;
        query.CollideWithBodies = true;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count > 0 && hit.TryGetValue("position", out var pos))
        {
            SetHit((Vector3)pos, hit.TryGetValue("collider", out var collider)
                ? collider.As<GodotObject>()
                : null);
        }
        else
        {
            SetHit(null, null);
        }
    }

    private void SetHit(Vector3? position, GodotObject collider)
    {
        WorldPosition = position;
        Collider = collider;
        marker.Visible = position.HasValue;
        if (position is { } p)
        {
            marker.GlobalPosition = p;
        }
    }

    private static Npc FindNpc(Node node)
    {
        while (node != null)
        {
            if (node is Npc npc)
            {
                return npc;
            }
            node = node.GetParent();
        }
        return null;
    }
}
