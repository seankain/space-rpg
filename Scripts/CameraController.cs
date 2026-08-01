using Godot;
using System;

public enum CameraState
{
    Freelook,
    Forward,
    Rear,
    Spinning
}

public partial class CameraController : Node3D
{
    [Export]
    public float TiltMax = 75f;
    [Export]
    public float MouseSensitivity = 0.1f;

    [Export]
    private float DurationToSnap = 1.0f;

    private double freelookIdleElapsed = 0f;

    private CameraState currentState = CameraState.Rear;

    private Vector3 defaultRotation;

    public override void _Ready()
    {
        defaultRotation = this.Rotation;
    }


    public override void _UnhandledInput(InputEvent @event)
    {
        // Only steer the camera while the mouse is captured for gameplay;
        // when a menu or dialogue has released the cursor, moving it toward
        // a button shouldn't spin the view.
        if (Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            return;
        }
        // Mouse capture isn't proof it's gameplay any more: the editor's free
        // camera captures the pointer while its look button is held, and that
        // turn belongs to it — without this the player's rig would spin along
        // behind the scenes and stay crooked after editor mode ends.
        if (DevConsole.BlocksGameplay)
        {
            return;
        }
        if (@event is InputEventMouseMotion)
        {
            currentState = CameraState.Freelook;
            freelookIdleElapsed = 0f;
            var mouseMotionEvent = (InputEventMouseMotion)@event;
            var rot = this.Rotation;
            rot.X -= mouseMotionEvent.Relative.Y * MouseSensitivity;
            rot.X = Mathf.Clamp(rot.X, Mathf.DegToRad(-TiltMax), Mathf.DegToRad(TiltMax));
            rot.Y += -mouseMotionEvent.Relative.X * MouseSensitivity;
            this.Rotation = rot;
        }
    }

    public override void _Process(double delta)
    {
        // switch (currentState)
        // {
        //     case CameraState.Freelook:
        //         freelookIdleElapsed += delta;
        //         if (freelookIdleElapsed >= DurationToSnap)
        //         {
        //             freelookIdleElapsed = 0;
        //             SnapToDefault();
        //         }
        //         break;
        //     case CameraState.Forward:
        //         break;
        //     case CameraState.Rear:
        //         break;
        //     case CameraState.Spinning:
        //         DoIdleRotation(delta);
        //         break;
        //     default:
        //         break;
        // }
    }


    public void SnapToDefault()
    {
        this.Rotation = defaultRotation;
        this.currentState = CameraState.Forward;
    }

    public void StartIdleRotation()
    {
        this.currentState = CameraState.Spinning;
    }


    public void DoIdleRotation(double delta)
    {
        var rot = this.Rotation;
        //rot.X -= (1 * (float)delta);
        //rot.X = Mathf.Clamp(this.Rotation.X, -TiltMax, TiltMax);
        rot.Y += (1 * (float)delta);
        rot.Y = Mathf.Clamp(rot.Y, 0, 360);
        this.Rotation = rot;
    }



}
