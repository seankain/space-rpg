using System;

// A frame's requested movement in the fly camera's own axes: +Z forward along
// the look direction, +X right, +Y world-up. Separated from Godot's Vector3 so
// the input-to-direction rules are unit-testable.
public readonly struct FlyMove
{
    public FlyMove(float right, float up, float forward)
    {
        Right = right;
        Up = up;
        Forward = forward;
    }

    public float Right { get; }
    public float Up { get; }
    public float Forward { get; }

    public bool IsZero => Right == 0f && Up == 0f && Forward == 0f;
}

// Engine-free rules behind the editor-mode free camera (EditorFlyCamera):
// which way a set of held keys flies, how the scroll wheel retunes the flight
// speed, and how far the view may pitch. Kept out of the Godot node for the
// same reason PlacementMath is — the arithmetic that decides where the author
// ends up is worth testing without an engine.
public static class FlyCameraMath
{
    // Flight speed bounds, in metres per second. The default is a brisk walk
    // so short hops between props feel controllable; the ceiling crosses a
    // 64-unit chunk in about half a second.
    public const float MinSpeed = 0.5f;
    public const float MaxSpeed = 120f;
    public const float DefaultSpeed = 8f;

    // Held boost (Shift) multiplier — enough to cross the level quickly
    // without having to retune the base speed and put it back.
    public const float BoostMultiplier = 4f;

    // One scroll notch scales the speed by this factor, so the usable range
    // is a couple of flicks rather than a hundred linear steps.
    public const float SpeedStep = 1.25f;

    // Straight up/down would gimbal-lock the yaw and let the author lose which
    // way is north; stop just short, the way every editor viewport does.
    public const float MaxPitchDegrees = 89f;

    // The direction the held keys ask for, in camera-local axes. Horizontal
    // input is normalized so a diagonal isn't faster than a straight line;
    // vertical (jump/ctrl) is independent of it and stays world-aligned, so
    // "up" is up even when the view is angled at the floor.
    public static FlyMove MoveDirection(
        bool forward, bool back, bool left, bool right, bool up, bool down)
    {
        var x = (right ? 1f : 0f) - (left ? 1f : 0f);
        var z = (forward ? 1f : 0f) - (back ? 1f : 0f);
        var length = MathF.Sqrt(x * x + z * z);
        if (length > 0f)
        {
            x /= length;
            z /= length;
        }
        var y = (up ? 1f : 0f) - (down ? 1f : 0f);
        return new FlyMove(x, y, z);
    }

    // Retune the flight speed by whole scroll notches (positive = faster),
    // geometrically so the control feels the same at 1 m/s and at 100.
    public static float AdjustSpeed(float speed, int notches)
    {
        var adjusted = speed * MathF.Pow(SpeedStep, notches);
        return Math.Clamp(adjusted, MinSpeed, MaxSpeed);
    }

    public static float ClampPitchDegrees(float degrees) =>
        Math.Clamp(degrees, -MaxPitchDegrees, MaxPitchDegrees);

    // Yaw wrapped into (-180, 180] so it never drifts into the range where
    // float precision starts costing the author degrees per revolution.
    public static float WrapYawDegrees(float degrees)
    {
        var wrapped = degrees % 360f;
        if (wrapped > 180f)
        {
            wrapped -= 360f;
        }
        else if (wrapped <= -180f)
        {
            wrapped += 360f;
        }
        return wrapped;
    }

    // Metres travelled this frame along one axis of MoveDirection.
    public static float FrameDistance(float speed, bool boosting, float delta) =>
        speed * (boosting ? BoostMultiplier : 1f) * delta;
}
