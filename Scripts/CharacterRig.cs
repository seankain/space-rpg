using Godot;

// Root script for the rig wrapper scenes under Scenes/Characters/Rigs/: one
// KayKit character model plus an AnimationPlayer already wired to its
// skeleton (docs/plans/npc-composition.md Phase 1). Consumers — Npc,
// PartyMemberFollower, BattleScene — instance a rig and play clips instead
// of re-doing forward flips and skeleton retargeting at runtime.
//
// Conventions every wrapper scene follows:
// - The wrapper root's visual forward is -Z, Godot's forward
//   (Vector3.Forward): the raw KayKit scenes face +Z, and the 180° flip
//   baked into the Model child turns them around to match the root's
//   forward vector. Consumers that rotate the rig (FacePlayer,
//   TurnMeshToward, battle lines) must therefore aim the root's -Z at
//   whatever the character should look at. (Player.tscn is different: it
//   rotates its Knight model node directly, which replaces the baked flip,
//   so its visual forward stays the raw model's +Z.)
// - The AnimationPlayer's root is the Model child, because the shared
//   library's clips address the skeleton as "Rig_Medium/Skeleton3D/...".
public partial class CharacterRig : Node3D
{
	public const string LibraryName = "player_animation_library";
	public const string IdleClip = "Idle_A";
	public const string RunClip = "Running_A";
	public const string WalkClip = "Walking_A";

	// Every clip in the KayKit set, as individual resources. The wrapper
	// scenes' library holds only the locomotion handful; anything else (the
	// battle idle/death, a dialogue gesture) is loaded from here on demand.
	public const string ClipDirectory = "res://ThirdParty/AnimationLibrary";

	// Library the on-demand clips are added to, kept apart from the wrapper
	// scenes' own so nothing here can shadow a locomotion clip.
	private const string ExtraLibraryName = "extra_animation_library";

	[Export]
	public AnimationPlayer Anim;

	// Plays a clip from the shared library; callers with their own library
	// (BattleScene) add it to Anim and address it by full name instead.
	public void Play(string clip) => Anim.Play($"{LibraryName}/{clip}");

	// Plays any clip from ClipDirectory, looping or once (dialogue gestures,
	// npc-dialogue-yarn.md Phase 4). Returns false when the clip can't be
	// loaded, so a caller can report it instead of standing there silently.
	// The clip is duplicated per loop mode and cached, so a rig can hold both
	// the looping and one-shot form of the same gesture.
	public bool PlayExtra(string clip, bool loop)
	{
		var name = ExtraClipName(clip, loop);
		if (!Anim.HasAnimationLibrary(ExtraLibraryName))
		{
			Anim.AddAnimationLibrary(ExtraLibraryName, new AnimationLibrary());
		}
		var library = Anim.GetAnimationLibrary(ExtraLibraryName);
		if (!library.HasAnimation(name))
		{
			var loaded = GD.Load<Animation>($"{ClipDirectory}/{clip}.res");
			if (loaded == null)
			{
				return false;
			}
			// Duplicated before the loop mode is set so the cached resource
			// other scenes load stays untouched (same care BattleScene takes).
			var animation = (Animation)loaded.Duplicate();
			animation.LoopMode = loop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
			library.AddAnimation(name, animation);
		}
		Anim.Play($"{ExtraLibraryName}/{name}");
		return true;
	}

	private static string ExtraClipName(string clip, bool loop) => loop ? $"{clip}__loop" : clip;
}
