using Godot;
using System.Linq;

// Walks a waypoint loop (npc-system plan Phase 3's Patrol mode). Waypoints
// are authored in the definition in the same local space as LocalPosition
// (chunk-local / interior-local) and resolved against the NPC's parent when
// the behavior attaches.
public partial class PatrolBehavior : NpcBehavior
{
	// From NpcDefinition.PatrolPoints.
	public Vector3[] LocalPoints = System.Array.Empty<Vector3>();

	private Vector3[] points = System.Array.Empty<Vector3>();
	private int index;

	public override void _Ready()
	{
		base._Ready();
		points = Body.GetParent() is Node3D parent
			? LocalPoints.Select(parent.ToGlobal).ToArray()
			: LocalPoints;
		StartTarget();
	}

	protected override void Tick(double delta)
	{
		if (points.Length == 0)
		{
			Halt(delta);
			return;
		}
		if (StepToward(points[index], delta) || TargetTimedOut)
		{
			index = (index + 1) % points.Length;
			StartTarget();
		}
	}
}
