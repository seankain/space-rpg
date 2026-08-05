using Godot;
using System.Collections.Generic;
using System.Linq;

// The one NPC script (docs/plans/npc-composition.md Phase 2): a friendly,
// stationary character the player can talk to. Builds its own interaction
// zone and "[E] Talk" prompt in code, and composes its conversation from the
// definition's NpcRole resources at interact time — no available roles gets
// a wave-off line, one role plays its dialogue directly, several roles offer
// a choice menu. Role subclasses and per-role scene variants are gone.
//
// NPCs are spawned from NpcDefinition resources (NpcSpawner.Spawn calls
// Initialize before the node enters the tree); the exports remain as
// fallbacks for hand-placed instances, but the definition wins when present.
public partial class Npc : CharacterBody3D
{
	// Every spawned NPC joins this group, so anything that needs "the live
	// NPCs" — the quest-marker locator asking where an NPC actually is right
	// now — can find them without walking the level tree.
	public const string GroupName = "Npc";

	[Export]
	public string DisplayName = "NPC";

	// Placeholder capsule tint for NPCs without a Rig.
	[Export]
	public Color BodyColor = Colors.White;

	[Export]
	public float InteractRadius = 2.5f;

	// Height above the NPC's origin where the interaction hint floats.
	[Export]
	public float PromptHeight = 2.2f;

	// The data file this NPC was spawned from; null for hand-placed nodes.
	// Shared cached resource — read, never write.
	public NpcDefinition Definition { get; private set; }

	// Stable id for saves, quests, and encounters. Hand-placed NPCs without
	// a definition fall back to their display name, the legacy key.
	public string NpcId =>
		string.IsNullOrEmpty(Definition?.NpcId) ? DisplayName : Definition.NpcId;

	private bool playerInRange;
	private Node3D player;
	private InteractionPrompt prompt;
	private bool despawnWhenDialogueEnds;
	private CharacterRig rig;
	// True while a play_anim gesture owns the rig (see PlayDialogueAnimation).
	private bool dialogueAnimation;

	// Mutable per-NPC state the shared role resources can't hold themselves
	// (a shopkeeper's Merchant), keyed by the role that created it.
	private readonly Dictionary<NpcRole, object> roleStates = new();

	private IEnumerable<NpcRole> Roles =>
		(Definition?.Roles ?? System.Array.Empty<NpcRole>()).Where(r => r != null);

	// Called by the spawner before this node enters the tree, so role
	// runtime state built in _Ready can already rely on the definition.
	public void Initialize(NpcDefinition definition)
	{
		Definition = definition;
		if (definition == null)
		{
			return;
		}
		if (!string.IsNullOrEmpty(definition.DisplayName))
		{
			DisplayName = definition.DisplayName;
		}
		BodyColor = definition.BodyColor;
	}

	public override void _Ready()
	{
		AddToGroup(GroupName);
		if (Definition?.Rig is { } rigScene)
		{
			AddRig(rigScene);
		}
		else if (GetNodeOrNull<MeshInstance3D>("MeshInstance3D") is { } mesh)
		{
			mesh.MaterialOverride = new StandardMaterial3D { AlbedoColor = BodyColor };
		}

		foreach (var role in Roles)
		{
			if (role.CreateRuntimeState(this) is { } state)
			{
				roleStates[role] = state;
			}
		}

		switch (Definition?.Behavior)
		{
			case NpcBehaviorMode.Wander:
				AddChild(new WanderBehavior { Radius = Definition.WanderRadius });
				break;
			case NpcBehaviorMode.Patrol:
				AddChild(new PatrolBehavior
				{
					LocalPoints = Definition.PatrolPoints ?? System.Array.Empty<Vector3>(),
				});
				break;
		}

		prompt = new InteractionPrompt
		{
			ActionName = "Interact",
			ActionDescription = $"Talk to {DisplayName}",
			Position = new Vector3(0, PromptHeight, 0),
			Visible = false,
		};
		AddChild(prompt);

		var zone = new Area3D();
		zone.AddChild(new CollisionShape3D
		{
			Shape = new SphereShape3D { Radius = InteractRadius },
			Position = new Vector3(0, 1, 0),
		});
		AddChild(zone);
		zone.BodyEntered += body =>
		{
			if (body.IsInGroup(Player.GroupName))
			{
				playerInRange = true;
				player = body as Node3D;
				prompt.Visible = true;
			}
		};
		zone.BodyExited += body =>
		{
			if (body.IsInGroup(Player.GroupName))
			{
				playerInRange = false;
				player = null;
				prompt.Visible = false;
			}
		};
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (playerInRange && !UiWindowManager.BlocksGameplay && @event.IsActionPressed("Interact"))
		{
			GetViewport().SetInputAsHandled();
			FacePlayer();
			prompt.Visible = false;
			// Logged as the conversation opens rather than when it closes, so
			// the entry lands ahead of whatever the conversation goes on to
			// give the player.
			SaveManager.Instance?.CurrentState?.RecordEvent(
				GameEventKind.Conversation, $"Spoke with {DisplayName}.");
			DialogueManager.Instance.Start(ComposeDialogue(), OnDialogueEnded);
		}
	}

	// The runtime state a role created for this NPC in _Ready, or null.
	public object GetRoleState(NpcRole role) => roleStates.GetValueOrDefault(role);

	// The first role runtime state of type T, or null — how a dialogue effect
	// finds this NPC's Merchant without knowing which role made it.
	public T GetRoleState<T>() where T : class => roleStates.Values.OfType<T>().FirstOrDefault();

	// Builds the play context a role's dialogue graph compiles against: this
	// NPC's name for the "$npc" speaker token, the effect host that bridges
	// scene actions (battle, shop, recruit) to the live node, and a warning
	// sink. State is the caller's game state (never null here).
	public DialogueContext CreateDialogueContext(GameState state) => new DialogueContext
	{
		State = state,
		SpeakerName = DisplayName,
		Host = new NpcDialogueHost(this, state),
		LogWarning = message => GD.PushWarning(message),
	};

	// Behaviors move the body only while nobody is engaging the NPC: halting
	// when the player is in range keeps the talk prompt catchable, and any open
	// window freezes ambient walkers — a conversation, a menu the player is
	// reading, or editor mode holding the world still so placement targets
	// don't wander off.
	public bool CanBehaviorMove => !playerInRange && !UiWindowManager.BlocksGameplay;

	// Locomotion animation for behaviors; capsule NPCs have no rig and
	// simply slide. A gesture a conversation asked for outranks it: behaviors
	// call this every physics frame (Halt does, even while the body is frozen
	// for a dialogue), so without the guard a play_anim clip would be stomped
	// by idle on the very next frame.
	public void PlayLocomotion(bool moving)
	{
		if (dialogueAnimation)
		{
			return;
		}
		rig?.Play(moving ? CharacterRig.WalkClip : CharacterRig.IdleClip);
	}

	// play_anim:<clip>[:loop] from the running conversation
	// (npc-dialogue-yarn.md Phase 4). A one-shot clip returns the NPC to idle
	// when it finishes; a looping one holds until the conversation closes or
	// another gesture replaces it. An NPC still wearing the placeholder capsule
	// has nothing to animate and silently doesn't gesture, exactly as it
	// silently doesn't walk.
	public void PlayDialogueAnimation(string clip, bool loop)
	{
		if (rig == null)
		{
			return;
		}
		if (!rig.PlayExtra(clip, loop))
		{
			GD.PushWarning($"Npc '{DisplayName}' could not load dialogue clip '{clip}'.");
			return;
		}
		dialogueAnimation = true;
	}

	// Back to the neutral stand, whether the gesture ran out or the
	// conversation closed under a looping one.
	private void EndDialogueAnimation()
	{
		if (!dialogueAnimation)
		{
			return;
		}
		dialogueAnimation = false;
		rig?.Play(CharacterRig.IdleClip);
	}

	// Roles whose actions remove the NPC from the world (a recruit joining)
	// call this so the body lingers until the conversation closes.
	public void DespawnWhenDialogueEnds() => despawnWhenDialogueEnds = true;

	// The merge rules from the composition plan: zero available roles is a
	// wave-off, one plays directly (every pre-composition conversation is
	// preserved verbatim), several get a choice menu.
	private DialogueLine ComposeDialogue()
	{
		var state = SaveManager.Instance?.CurrentState;
		var available = new List<NpcRole>();
		if (state != null)
		{
			available.AddRange(Roles.Where(r => r.IsAvailable(this, state)));
		}
		else if (Roles.Any())
		{
			GD.PushWarning($"Npc '{DisplayName}' has roles but no game state; falling back to small talk.");
		}
		if (available.Count == 1)
		{
			return available[0].BuildDialogue(this, state);
		}
		if (available.Count > 1)
		{
			var choices = available
				.Select(role => new DialogueChoice
				{
					Label = role.MenuLabel,
					Next = role.BuildDialogue(this, state),
				})
				.ToList();
			// No Next: picking it just closes the conversation.
			choices.Add(new DialogueChoice { Label = "Never mind" });
			return new DialogueLine
			{
				Speaker = DisplayName,
				Text = "What can I do for you?",
				Choices = choices,
			};
		}
		return new DialogueLine
		{
			Speaker = DisplayName,
			Text = "Hello there.",
		};
	}

	private void OnDialogueEnded()
	{
		EndDialogueAnimation();
		if (despawnWhenDialogueEnds)
		{
			if (!IsQueuedForDeletion())
			{
				QueueFree();
			}
			return;
		}
		ShowPromptIfPlayerInRange();
	}

	// Returns the talk hint when a conversation (or the shop it opened)
	// closes with the player still nearby. Public so role actions can hand
	// it to other UIs as their on-closed callback.
	public void ShowPromptIfPlayerInRange()
	{
		if (playerInRange && !IsQueuedForDeletion())
		{
			prompt.Visible = true;
		}
	}

	// Swaps the placeholder capsule for the definition's rig wrapper, which
	// arrives facing the body's -Z forward and animation-wired
	// (CharacterRig).
	private void AddRig(PackedScene rigScene)
	{
		rig = rigScene.Instantiate<CharacterRig>();
		AddChild(rig);
		if (GetNodeOrNull<MeshInstance3D>("MeshInstance3D") is { } capsule)
		{
			capsule.QueueFree();
		}
		// A one-shot dialogue gesture hands the rig back when it runs out.
		// Stationary NPCs never call PlayLocomotion, so this — not the
		// behaviors — is what unfreezes them from a gesture's last pose.
		rig.Anim.AnimationFinished += _ => EndDialogueAnimation();
		rig.Play(CharacterRig.IdleClip);
	}

	private void FacePlayer()
	{
		if (player == null)
		{
			return;
		}
		var toPlayer = player.GlobalPosition - GlobalPosition;
		if (toPlayer.LengthSquared() < 0.0001f)
		{
			return;
		}
		// A rig's visual forward is the body's -Z (CharacterRig convention),
		// so aim -Z at the player. Capsule placeholders have no visual
		// forward and turn harmlessly.
		var rotation = Rotation;
		rotation.Y = Mathf.Atan2(-toPlayer.X, -toPlayer.Z);
		Rotation = rotation;
	}
}
