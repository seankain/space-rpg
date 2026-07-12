using Godot;
using System;

public partial class Player : CharacterBody3D
{
	public const string GroupName = "Player";
	public const float Speed = 5.0f;
	public const float JumpVelocity = 4.5f;
	public const float TurnSpeed = 10.0f;

	[Export]
	public AnimationPlayer anim;

	[Export]
	public Node3D cameraRig;

	[Export]
	public Node3D meshRoot;

    public override void _Ready()
    {
		AddToGroup(GroupName);
        this.anim.Play("player_animation_library/Idle_A");
    }

    public override void _Process(double delta)
	{
		// I just put this here to show my son some animations, move to AnimationTree
		if(this.Velocity.Length() > 0.0)
		{
			this.anim.Play("player_animation_library/Running_A");
		}
		else
		{
			this.anim.Play("player_animation_library/Idle_A");
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		Vector3 velocity = Velocity;

		// Add the gravity.
		if (!IsOnFloor())
		{
			velocity += GetGravity() * (float)delta;
		}

		// Conversations lock movement; polled input would otherwise still fire
		// (e.g. Space both confirms a dialogue choice and jumps).
		bool inputLocked = DialogueManager.IsDialogueActive;

		// Handle Jump.
		if (!inputLocked && Input.IsActionJustPressed("Jump") && IsOnFloor())
		{
			velocity.Y = JumpVelocity;
		}

		// Get the input direction and handle the movement/deceleration.
		// As good practice, you should replace UI actions with custom gameplay actions.
		Vector2 inputDir = inputLocked ? Vector2.Zero : Input.GetVector("Left", "Right", "Forward", "Backward");
		// Rotate the input by the camera's yaw so "forward" is wherever the camera points.
		Vector3 direction = new Vector3(inputDir.X, 0, inputDir.Y).Rotated(Vector3.Up, cameraRig.GlobalRotation.Y).Normalized();
		if (direction != Vector3.Zero)
		{
			velocity.X = direction.X * Speed;
			velocity.Z = direction.Z * Speed;
			TurnMeshToward(direction, (float)delta);
		}
		else
		{
			velocity.X = Mathf.MoveToward(Velocity.X, 0, Speed);
			velocity.Z = Mathf.MoveToward(Velocity.Z, 0, Speed);
		}

		Velocity = velocity;
		MoveAndSlide();
	}

	private void TurnMeshToward(Vector3 direction, float delta)
	{
		// The Knight model's visual forward is its local +Z, so aim +Z at the movement direction.
		float targetYaw = Mathf.Atan2(direction.X, direction.Z);
		Vector3 meshRotation = meshRoot.Rotation;
		meshRotation.Y = Mathf.LerpAngle(meshRotation.Y, targetYaw, 1f - Mathf.Exp(-TurnSpeed * delta));
		meshRoot.Rotation = meshRotation;
	}
}
