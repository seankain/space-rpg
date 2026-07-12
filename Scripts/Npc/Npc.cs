using Godot;

// Base script for Scenes/Npc.tscn: a friendly, stationary NPC the player can
// talk to (docs/plans/npc-system.md Phase 1). Builds its own interaction zone
// and "[E] Talk" prompt in code so scene instances only need a script + a few
// exported properties. Subclasses override OnInteract to start their dialogue.
public partial class Npc : CharacterBody3D
{
	[Export]
	public string DisplayName = "NPC";

	// Placeholder capsule tint until NPCs get real models.
	[Export]
	public Color BodyColor = Colors.White;

	[Export]
	public float InteractRadius = 2.5f;

	// Height above the NPC's origin where the interaction hint floats.
	[Export]
	public float PromptHeight = 2.2f;

	private bool playerInRange;
	private Node3D player;
	private InteractionPrompt prompt;

	public override void _Ready()
	{
		if (GetNodeOrNull<MeshInstance3D>("MeshInstance3D") is { } mesh)
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
		if (playerInRange && !DialogueManager.IsDialogueActive && @event.IsActionPressed("Interact"))
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
