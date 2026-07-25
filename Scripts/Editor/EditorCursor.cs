using Godot;

// The editor-mode placement marker (in-game-editor plan Phase 2). While editor
// mode is active DevConsole parents one of these under the scene root; each
// physics frame it casts a ray from the active camera through the screen center
// into world geometry and parks a translucent marker at the hit point. The hit
// position is what the `here` token (Phase 3) resolves to and what the editor
// HUD reports.
public partial class EditorCursor : Node3D
{
    private const float RayLength = 1000f;

    private MeshInstance3D marker;

    // The current world hit point, or null when the ray misses (or there is no
    // camera). DevConsole reads this for the HUD and the `here` token.
    public Vector3? WorldPosition { get; private set; }

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
        var camera = GetViewport()?.GetCamera3D();
        if (camera == null)
        {
            SetHit(null);
            return;
        }
        var screenCenter = GetViewport().GetVisibleRect().Size * 0.5f;
        var from = camera.ProjectRayOrigin(screenCenter);
        var to = from + camera.ProjectRayNormal(screenCenter) * RayLength;
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        SetHit(hit.Count > 0 && hit.TryGetValue("position", out var pos) ? (Vector3)pos : null);
    }

    private void SetHit(Vector3? position)
    {
        WorldPosition = position;
        marker.Visible = position.HasValue;
        if (position is { } p)
        {
            marker.GlobalPosition = p;
        }
    }
}
