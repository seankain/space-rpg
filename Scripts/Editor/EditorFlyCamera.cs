using Godot;

// The controller the author inhabits in editor mode: a free-floating camera
// detached from the player body. WASD flies along the look direction, Jump
// (Space) rises and EditorDown (Ctrl) sinks — both world-vertical, so "up" is
// up even with the view angled at the floor — Shift boosts, and holding the
// right mouse button steers.
//
// The pointer stays free the rest of the time, because editor mode is a
// point-and-click surface now: the toolbar on the right and clicking NPCs and
// ground in the level both need a cursor. That is why look is on a held
// button rather than permanent capture, the way every editor viewport works.
//
// DevConsole owns the lifecycle: it parents one of these under the scene root
// when editor mode turns on and frees it when it turns off, and this node
// makes itself the current camera and hands the view back on the way out.
public partial class EditorFlyCamera : Camera3D
{
    // Degrees of rotation per pixel of mouse movement while steering, matching
    // CameraController.MouseSensitivity so the two views feel the same.
    private const float LookSensitivity = 0.1f;

    // Where the view was before editor mode took over, so leaving editor mode
    // puts the player's camera back rather than leaving a dead viewport.
    private Camera3D previousCamera;

    // Look angles in degrees, kept here rather than read back off the basis so
    // repeated small mouse deltas can't accumulate roll.
    private float yawDegrees;
    private float pitchDegrees;

    private bool steering;

    // Current flight speed in m/s, retuned with the scroll wheel while
    // steering. DevConsole's HUD reports it.
    public float Speed { get; private set; } = FlyCameraMath.DefaultSpeed;

    public override void _Ready()
    {
        previousCamera = GetViewport()?.GetCamera3D();
        // Start exactly where the author was looking from, so entering editor
        // mode reframes nothing — it just unhooks the camera from the player.
        if (previousCamera != null)
        {
            GlobalPosition = previousCamera.GlobalPosition;
            var rotation = previousCamera.GlobalRotationDegrees;
            yawDegrees = FlyCameraMath.WrapYawDegrees(rotation.Y);
            pitchDegrees = FlyCameraMath.ClampPitchDegrees(rotation.X);
        }
        ApplyLook();
        MakeCurrent();
    }

    public override void _ExitTree()
    {
        // Deliberately not StopSteering: this runs a frame after DevConsole
        // queued the free and already decided what the pointer should be, so
        // restoring "visible" here would undo that. Only the camera is ours.
        steering = false;
        // Current is exclusive per viewport, so the outgoing camera has to
        // hand the flag back explicitly; a freed camera just leaves none.
        if (previousCamera != null && IsInstanceValid(previousCamera))
        {
            previousCamera.MakeCurrent();
        }
    }

    // Once the view is grabbed the turn belongs to the camera: read steering
    // ahead of GUI input so dragging across the toolbar (or any other panel)
    // can't swallow half a turn or strand the pointer captured.
    public override void _Input(InputEvent @event)
    {
        if (!steering)
        {
            return;
        }
        if (@event is InputEventMouseMotion motion)
        {
            yawDegrees = FlyCameraMath.WrapYawDegrees(
                yawDegrees - motion.Relative.X * LookSensitivity);
            pitchDegrees = FlyCameraMath.ClampPitchDegrees(
                pitchDegrees - motion.Relative.Y * LookSensitivity);
            ApplyLook();
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false })
        {
            StopSteering();
            GetViewport().SetInputAsHandled();
        }
    }

    // Starting a turn, on the other hand, stays behind the GUI — a right-click
    // on a panel is that panel's to handle.
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton button)
        {
            HandleMouseButton(button);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        // The console owns the keyboard while it is open; flying on WASD typed
        // into the command line is the same bug Player guards against. So do
        // the editor forms — the NPC sheet, and the dialogue editor it opens
        // over itself, which is nothing but text fields.
        if (DevConsole.EditorPanelOpen)
        {
            return;
        }
        var move = FlyCameraMath.MoveDirection(
            forward: Input.IsActionPressed("Forward"),
            back: Input.IsActionPressed("Backward"),
            left: Input.IsActionPressed("Left"),
            right: Input.IsActionPressed("Right"),
            up: Input.IsActionPressed("Jump"),
            down: Input.IsActionPressed("EditorDown"));
        if (move.IsZero)
        {
            return;
        }
        var distance = FlyCameraMath.FrameDistance(
            Speed, Input.IsActionPressed("EditorBoost"), (float)delta);
        // Horizontal input rides the camera's own axes (fly where you look);
        // vertical stays on world up so Ctrl/Space are never diagonal.
        var basis = GlobalTransform.Basis;
        var direction = basis.X * move.Right - basis.Z * move.Forward + Vector3.Up * move.Up;
        GlobalPosition += direction * distance;
    }

    private void HandleMouseButton(InputEventMouseButton button)
    {
        if (button.ButtonIndex == MouseButton.Right)
        {
            // The matching release is claimed in _Input; only the grab is here.
            if (button.Pressed)
            {
                StartSteering();
                GetViewport().SetInputAsHandled();
            }
            return;
        }
        // Scroll retunes the flight speed, but only while steering — otherwise
        // it would fight the editor panels' scroll containers.
        if (!steering || !button.Pressed)
        {
            return;
        }
        var notches = button.ButtonIndex switch
        {
            MouseButton.WheelUp => 1,
            MouseButton.WheelDown => -1,
            _ => 0,
        };
        if (notches != 0)
        {
            Speed = FlyCameraMath.AdjustSpeed(Speed, notches);
            GetViewport().SetInputAsHandled();
        }
    }

    private void StartSteering()
    {
        steering = true;
        // Captured while steering so the pointer can't run off the window
        // mid-turn; released the moment the button comes back up.
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private void StopSteering()
    {
        if (!steering)
        {
            return;
        }
        steering = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private void ApplyLook() =>
        GlobalRotationDegrees = new Vector3(pitchDegrees, yawDegrees, 0f);
}
