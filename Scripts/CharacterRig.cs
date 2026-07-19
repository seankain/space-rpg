using Godot;

// Root script for the rig wrapper scenes under Scenes/Characters/Rigs/: one
// KayKit character model plus an AnimationPlayer already wired to its
// skeleton (docs/plans/npc-composition.md Phase 1). Consumers — Npc,
// PartyMemberFollower, BattleScene — instance a rig and play clips instead
// of re-doing forward flips and skeleton retargeting at runtime.
//
// Conventions every wrapper scene follows:
// - The wrapper root's visual forward is +Z: the KayKit model's 180° flip is
//   baked into the Model child, matching Player.tscn's Knight, so rotating
//   the rig (FacePlayer, TurnMeshToward, battle lines) behaves uniformly.
// - The AnimationPlayer's root is the Model child, because the shared
//   library's clips address the skeleton as "Rig_Medium/Skeleton3D/...".
public partial class CharacterRig : Node3D
{
	public const string LibraryName = "player_animation_library";
	public const string IdleClip = "Idle_A";
	public const string RunClip = "Running_A";
	public const string WalkClip = "Walking_A";

	[Export]
	public AnimationPlayer Anim;

	// Plays a clip from the shared library; callers with their own library
	// (BattleScene) add it to Anim and address it by full name instead.
	public void Play(string clip) => Anim.Play($"{LibraryName}/{clip}");
}
