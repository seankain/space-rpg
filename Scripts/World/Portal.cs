using Godot;

// A portal between top-level game areas (root scenes like Intro.tscn and
// World1.tscn). It works like a Door: pressing Interact in range swaps the
// running level through LevelManager.StartLevel, and the destination's own
// ChunkManager streams in that area's chunks. Unlike a Door, portal travel is
// one-way area movement, not an interior visit: nothing is recorded in
// GameState.Return*, and the player arrives at the destination level's Spawn
// marker.
public partial class Portal : Node3D
{
	[Export(PropertyHint.File, "*.tscn")]
	public string TargetScenePath;

	// Shown in the prompt and used as the destination's LocationName.
	[Export]
	public string TargetDisplayName = "Unknown";

	[Export]
	public float InteractRadius = 2.0f;

	// Height above the portal's origin where the interaction hint floats.
	[Export]
	public float PromptHeight = 2.2f;

	private bool playerInRange;
	private InteractionPrompt prompt;

	public override void _Ready()
	{
		prompt = new InteractionPrompt
		{
			ActionName = "Interact",
			ActionDescription = $"Travel to {TargetDisplayName}",
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
				prompt.Visible = true;
			}
		};
		zone.BodyExited += body =>
		{
			if (body.IsInGroup(Player.GroupName))
			{
				playerInRange = false;
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
			Travel();
		}
	}

	private void Travel()
	{
		var state = SaveManager.Instance?.CurrentState;
		if (state == null)
		{
			GD.PushWarning($"Portal '{Name}' used with no game in progress.");
			return;
		}
		if (string.IsNullOrEmpty(TargetScenePath))
		{
			GD.PushWarning($"Portal '{Name}' has no TargetScenePath to load.");
			return;
		}
		state.CurrentLevelPath = TargetScenePath;
		state.LocationName = TargetDisplayName;
		// Arrive at the destination's Spawn marker, not the position carried
		// over from the previous area.
		state.PlayerPosition = null;
		state.PlayerRotation = null;
		LevelManager.Instance.StartLevel(state.CurrentLevelPath);
	}
}
