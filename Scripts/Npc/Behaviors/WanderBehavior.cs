using Godot;

// Meanders within a radius of the spawn point (npc-system plan Phase 3's
// Wander mode): walk to a random nearby point, linger a few seconds, pick
// another.
public partial class WanderBehavior : NpcBehavior
{
	// From NpcDefinition.WanderRadius.
	public float Radius = 4f;

	private Vector3 home;
	private Vector3 target;
	private bool hasTarget;
	private double lingerTimer;

	public override void _Ready()
	{
		base._Ready();
		home = Body.GlobalPosition;
	}

	protected override void Tick(double delta)
	{
		if (!hasTarget)
		{
			if (lingerTimer > 0)
			{
				lingerTimer -= delta;
				Halt(delta);
				return;
			}
			var angle = (float)GD.RandRange(0, Mathf.Tau);
			var distance = (float)GD.RandRange(Radius * 0.3f, Radius);
			target = home + new Vector3(Mathf.Sin(angle) * distance, 0, Mathf.Cos(angle) * distance);
			hasTarget = true;
			StartTarget();
		}
		if (StepToward(target, delta) || TargetTimedOut)
		{
			hasTarget = false;
			lingerTimer = GD.RandRange(2.0, 5.0);
		}
	}
}
