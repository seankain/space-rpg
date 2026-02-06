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
        if (@event is InputEventMouseMotion)
        {
            currentState = CameraState.Freelook;
            freelookIdleElapsed = 0f;
            var mouseMotionEvent = (InputEventMouseMotion)@event;
            var rot = this.Rotation;
            rot.X -= mouseMotionEvent.Relative.Y * MouseSensitivity;
            rot.X = Mathf.Clamp(this.Rotation.X, -TiltMax, TiltMax);
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
