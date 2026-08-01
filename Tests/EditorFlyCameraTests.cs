using Xunit;

// The editor-mode free camera's engine-free rules (in-game-editor plan
// Phase 7): what a set of held keys asks for, how the scroll wheel retunes the
// flight speed, and the angle limits. The Godot node that applies them
// (EditorFlyCamera) is exercised in the running editor.
public class EditorFlyCameraTests
{
    [Fact]
    public void NoKeysHeldIsNoMovement()
    {
        var move = FlyCameraMath.MoveDirection(false, false, false, false, false, false);
        Assert.True(move.IsZero);
    }

    [Theory]
    [InlineData(true, false, false, false, 0f, 1f)]   // W
    [InlineData(false, true, false, false, 0f, -1f)]  // S
    [InlineData(false, false, true, false, -1f, 0f)]  // A
    [InlineData(false, false, false, true, 1f, 0f)]   // D
    public void EachHorizontalKeyFliesItsOwnAxis(
        bool forward, bool back, bool left, bool right, float expectedRight, float expectedForward)
    {
        var move = FlyCameraMath.MoveDirection(forward, back, left, right, false, false);
        Assert.Equal(expectedRight, move.Right, 3);
        Assert.Equal(expectedForward, move.Forward, 3);
        Assert.Equal(0f, move.Up, 3);
    }

    [Fact]
    public void OpposingKeysCancel()
    {
        var move = FlyCameraMath.MoveDirection(
            forward: true, back: true, left: true, right: true, up: true, down: true);
        Assert.True(move.IsZero);
    }

    [Fact]
    public void DiagonalFlightIsNoFasterThanStraight()
    {
        var straight = FlyCameraMath.MoveDirection(true, false, false, false, false, false);
        var diagonal = FlyCameraMath.MoveDirection(true, false, false, true, false, false);
        var straightSpeed = straight.Right * straight.Right + straight.Forward * straight.Forward;
        var diagonalSpeed = diagonal.Right * diagonal.Right + diagonal.Forward * diagonal.Forward;
        Assert.Equal(straightSpeed, diagonalSpeed, 3);
    }

    [Fact]
    public void JumpRisesAndCtrlSinksIndependentlyOfTheHorizontalInput()
    {
        var up = FlyCameraMath.MoveDirection(true, false, false, false, up: true, down: false);
        Assert.Equal(1f, up.Up, 3);
        // Rising doesn't cost the forward component any speed — vertical is a
        // separate world-up axis, not part of the normalized horizontal.
        Assert.Equal(1f, up.Forward, 3);

        var down = FlyCameraMath.MoveDirection(false, false, false, false, up: false, down: true);
        Assert.Equal(-1f, down.Up, 3);
    }

    [Fact]
    public void ScrollingUpSpeedsUpAndScrollingDownSlowsDown()
    {
        var faster = FlyCameraMath.AdjustSpeed(FlyCameraMath.DefaultSpeed, 1);
        var slower = FlyCameraMath.AdjustSpeed(FlyCameraMath.DefaultSpeed, -1);
        Assert.True(faster > FlyCameraMath.DefaultSpeed);
        Assert.True(slower < FlyCameraMath.DefaultSpeed);
        // Geometric, so a notch out and a notch back is where you started.
        Assert.Equal(FlyCameraMath.DefaultSpeed, FlyCameraMath.AdjustSpeed(faster, -1), 3);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(-60)]
    public void SpeedStaysInsideItsBounds(int notches)
    {
        var speed = FlyCameraMath.AdjustSpeed(FlyCameraMath.DefaultSpeed, notches);
        Assert.InRange(speed, FlyCameraMath.MinSpeed, FlyCameraMath.MaxSpeed);
    }

    [Fact]
    public void BoostMovesFurtherInTheSameFrame()
    {
        var normal = FlyCameraMath.FrameDistance(10f, boosting: false, delta: 0.5f);
        var boosted = FlyCameraMath.FrameDistance(10f, boosting: true, delta: 0.5f);
        Assert.Equal(5f, normal, 3);
        Assert.Equal(5f * FlyCameraMath.BoostMultiplier, boosted, 3);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(45f, 45f)]
    [InlineData(120f, FlyCameraMath.MaxPitchDegrees)]
    [InlineData(-120f, -FlyCameraMath.MaxPitchDegrees)]
    public void PitchStopsShortOfStraightUpAndDown(float input, float expected)
    {
        Assert.Equal(expected, FlyCameraMath.ClampPitchDegrees(input), 3);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(180f, 180f)]
    [InlineData(200f, -160f)]
    [InlineData(-200f, 160f)]
    [InlineData(720f, 0f)]
    public void YawWrapsIntoASingleTurn(float input, float expected)
    {
        Assert.Equal(expected, FlyCameraMath.WrapYawDegrees(input), 3);
    }
}
