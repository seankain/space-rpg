using Godot;

// Base of the ambient NPC behaviors (npc-composition plan Phase 4): child
// nodes attached to an Npc from its definition's Behavior mode, driving the
// body in _PhysicsProcess. The split this preserves: roles are interaction
// verbs (data-authorable resources, no per-frame work), behaviors are
// continuous processing (nodes). Interaction pauses behavior — the body
// halts while the player is in range or a dialogue is open, and resumes
// after.
//
// Movement is direct pursuit with gravity, the same stand-in the party
// followers use until levels get a navmesh bake; a per-target timeout keeps
// a snagged NPC from marching into a wall forever.
public abstract partial class NpcBehavior : Node
{
	// A stroll, well under the player's pace.
	protected const float WalkSpeed = 1.6f;
	protected const float TurnSpeed = 8f;
	protected const float ArriveDistance = 0.35f;
	// Give up on an unreachable target after this long and move on.
	protected const float TargetTimeout = 8f;

	protected Npc Body { get; private set; }

	private double targetAge;

	public override void _Ready()
	{
		Body = GetParent<Npc>();
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!Body.CanBehaviorMove)
		{
			Halt(delta);
			return;
		}
		targetAge += delta;
		Tick(delta);
	}

	protected abstract void Tick(double delta);

	// Call when a new movement target is chosen; restarts the
	// unreachable-target clock.
	protected void StartTarget() => targetAge = 0;

	protected bool TargetTimedOut => targetAge > TargetTimeout;

	// Walks toward a global position; returns true once arrived (halted).
	protected bool StepToward(Vector3 target, double delta)
	{
		var toTarget = target - Body.GlobalPosition;
		toTarget.Y = 0;
		if (toTarget.Length() <= ArriveDistance)
		{
			Halt(delta);
			return true;
		}
		var direction = toTarget.Normalized();
		var velocity = Body.Velocity;
		if (!Body.IsOnFloor())
		{
			velocity += Body.GetGravity() * (float)delta;
		}
		velocity.X = direction.X * WalkSpeed;
		velocity.Z = direction.Z * WalkSpeed;
		Body.Velocity = velocity;
		Body.MoveAndSlide();
		FaceToward(direction, delta);
		Body.PlayLocomotion(moving: true);
		return false;
	}

	// Decelerates to a standstill (gravity still applies) and idles.
	protected void Halt(double delta)
	{
		var velocity = Body.Velocity;
		if (!Body.IsOnFloor())
		{
			velocity += Body.GetGravity() * (float)delta;
		}
		velocity.X = Mathf.MoveToward(velocity.X, 0, WalkSpeed);
		velocity.Z = Mathf.MoveToward(velocity.Z, 0, WalkSpeed);
		Body.Velocity = velocity;
		Body.MoveAndSlide();
		Body.PlayLocomotion(moving: false);
	}

	private void FaceToward(Vector3 direction, double delta)
	{
		// The whole body turns; its visual forward is -Z (CharacterRig
		// convention), same as FacePlayer, so aim -Z along the walk direction.
		var rotation = Body.Rotation;
		var targetYaw = Mathf.Atan2(-direction.X, -direction.Z);
		rotation.Y = Mathf.LerpAngle(rotation.Y, targetYaw, 1f - Mathf.Exp(-TurnSpeed * (float)delta));
		Body.Rotation = rotation;
	}
}
