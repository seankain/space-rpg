using Godot;

// A recruited party member walking behind the leader (party plan Phase 2):
// the same Knight body and animations as Player.tscn but AI-driven. Simple
// direct pursuit with per-member spacing stands in for navmesh pathing until
// a level actually needs it, and a catch-up teleport self-heals whatever
// pursuit can't solve (stale saved positions, geometry snags, falls).
public partial class PartyMemberFollower : CharacterBody3D
{
	public const string GroupName = "PartyFollower";
	public const float Speed = Player.Speed;
	public const float TurnSpeed = Player.TurnSpeed;
	// Beyond this distance the follower gives up walking and snaps to the
	// leader — also what makes spawning at a saved position safe when the
	// save predates the current level layout.
	public const float CatchUpDistance = 16f;

	private const string ScenePath = "res://Scenes/PartyMemberFollower.tscn";

	[Export]
	public AnimationPlayer anim;

	[Export]
	public Node3D meshRoot;

	[Export]
	public Label3D nameLabel;

	// CharacterEntity.Id of the party member this body represents; how saves
	// and menus find the follower again.
	public ulong MemberId { get; set; }

	// Position in the follow line (1 = first behind the leader). Staggers
	// stop distances so followers queue up instead of shoving one another.
	public int FollowIndex { get; set; } = 1;

	private float StopDistance => 2.0f + 1.5f * (FollowIndex - 1);

	// Instances the follower scene for a party member. Used by Level when a
	// level loads/restores and by RecruitNpc when someone joins mid-play.
	public static PartyMemberFollower Spawn(Node parent, CharacterEntity member, int followIndex, Vector3 position)
	{
		var follower = GD.Load<PackedScene>(ScenePath).Instantiate<PartyMemberFollower>();
		follower.MemberId = member.Id;
		follower.FollowIndex = followIndex;
		parent.AddChild(follower);
		follower.GlobalPosition = position;
		follower.nameLabel.Text = member.Name;
		return follower;
	}

	public override void _Ready()
	{
		AddToGroup(GroupName);
		anim.Play("player_animation_library/Idle_A");
	}

	public override void _PhysicsProcess(double delta)
	{
		Vector3 velocity = Velocity;
		if (!IsOnFloor())
		{
			velocity += GetGravity() * (float)delta;
		}

		if (GetTree().GetFirstNodeInGroup(Player.GroupName) is Node3D leader)
		{
			var toLeader = leader.GlobalPosition - GlobalPosition;
			toLeader.Y = 0;
			var distance = toLeader.Length();
			if (distance > CatchUpDistance || GlobalPosition.Y < leader.GlobalPosition.Y - 20f)
			{
				GlobalPosition = leader.GlobalPosition + BehindLeaderOffset(FollowIndex);
				velocity = Vector3.Zero;
			}
			else if (distance > StopDistance)
			{
				var direction = toLeader.Normalized();
				velocity.X = direction.X * Speed;
				velocity.Z = direction.Z * Speed;
				TurnMeshToward(direction, (float)delta);
			}
			else
			{
				velocity.X = Mathf.MoveToward(velocity.X, 0, Speed);
				velocity.Z = Mathf.MoveToward(velocity.Z, 0, Speed);
			}
		}

		Velocity = velocity;
		MoveAndSlide();

		// Same placeholder two-state switch as Player until an AnimationTree.
		anim.Play(new Vector2(Velocity.X, Velocity.Z).Length() > 0.1f
			? "player_animation_library/Running_A"
			: "player_animation_library/Idle_A");
	}

	public static Vector3 BehindLeaderOffset(int followIndex) => new(0.6f * followIndex, 0, 1.5f * followIndex);

	private void TurnMeshToward(Vector3 direction, float delta)
	{
		// Knight's visual forward is local +Z, same as Player.
		float targetYaw = Mathf.Atan2(direction.X, direction.Z);
		Vector3 meshRotation = meshRoot.Rotation;
		meshRotation.Y = Mathf.LerpAngle(meshRotation.Y, targetYaw, 1f - Mathf.Exp(-TurnSpeed * delta));
		meshRoot.Rotation = meshRotation;
	}
}
