using Godot;

// Base script for Scenes/Npc.tscn: a friendly, stationary NPC the player can
// talk to (docs/plans/npc-system.md Phase 1). Builds its own interaction zone
// and "[E] Talk" prompt in code so scene instances only need a script + a few
// exported properties. Subclasses override OnInteract to start their dialogue.
//
// NPCs are spawned from NpcDefinition resources (NpcSpawner.Spawn calls
// Initialize before the node enters the tree); the exports remain as
// fallbacks for hand-placed instances, but the definition wins when present.
public partial class Npc : CharacterBody3D
{
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

	// Called by the spawner before this node enters the tree, so _Ready
	// overrides (defeated/recruited checks, the shopkeeper's Merchant) can
	// already rely on the definition.
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
		if (Definition?.Rig is { } rigScene)
		{
			AddRig(rigScene);
		}
		else if (GetNodeOrNull<MeshInstance3D>("MeshInstance3D") is { } mesh)
		{
			mesh.MaterialOverride = new StandardMaterial3D { AlbedoColor = BodyColor };
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
		if (playerInRange && !DialogueManager.IsDialogueActive && !ShopMenu.IsShopOpen
			&& @event.IsActionPressed("Interact"))
		{
			GetViewport().SetInputAsHandled();
			FacePlayer();
			prompt.Visible = false;
			OnInteract();
		}
	}

	// Default is a wave-off line; concrete NPCs override this with their
	// actual conversation.
	protected virtual void OnInteract()
	{
		DialogueManager.Instance.Start(new DialogueLine
		{
			Speaker = DisplayName,
			Text = "Hello there.",
		}, ShowPromptIfPlayerInRange);
	}

	// Pass as the DialogueManager onEnded callback so the talk hint returns
	// when the conversation closes with the player still nearby.
	protected void ShowPromptIfPlayerInRange()
	{
		if (playerInRange && !IsQueuedForDeletion())
		{
			prompt.Visible = true;
		}
	}

	// Swaps the placeholder capsule for the definition's rig wrapper, which
	// arrives forward-flipped and animation-wired (CharacterRig).
	private void AddRig(PackedScene rigScene)
	{
		var rig = rigScene.Instantiate<CharacterRig>();
		AddChild(rig);
		if (GetNodeOrNull<MeshInstance3D>("MeshInstance3D") is { } capsule)
		{
			capsule.QueueFree();
		}
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
		// Capsule placeholders have no visual forward yet, but turning the
		// body keeps this correct once real models replace them.
		var rotation = Rotation;
		rotation.Y = Mathf.Atan2(toPlayer.X, toPlayer.Z);
		Rotation = rotation;
	}
}
