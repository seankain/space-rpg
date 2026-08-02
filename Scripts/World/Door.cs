using Godot;

// An enterable doorway: the flat door plane on a building (or the exit wall
// of an interior) carries this script. Pressing Interact in range swaps the
// running level through LevelManager.StartLevel, so the loading screen shows
// for every transition. The interaction zone and "[E] Enter ..." prompt are
// built in code, same as Npc/Pickup, so scenes only place the node and set
// exports.
//
// An entrance door records where the player was standing in
// GameState.Return* before loading TargetScenePath; the interior's exit door
// (ReturnsToPrevious = true) consumes those fields to put the player back
// outside. Both sides live in GameState, so saving inside an interior and
// loading later still walks back out correctly.
public partial class Door : Node3D
{
	[Export(PropertyHint.File, "*.tscn")]
	public string TargetScenePath;

	// Shown in the prompt and used as the interior's LocationName.
	[Export]
	public string TargetDisplayName = "Shop";

	// Exit-door mode: ignore TargetScenePath and return to the level the
	// player entered this interior from.
	[Export]
	public bool ReturnsToPrevious;

	[Export]
	public float InteractRadius = 2.0f;

	// Height above the door's origin where the interaction hint floats.
	[Export]
	public float PromptHeight = 2.6f;

	private bool playerInRange;
	private InteractionPrompt prompt;

	public override void _Ready()
	{
		prompt = new InteractionPrompt
		{
			ActionName = "Interact",
			ActionDescription = ReturnsToPrevious ? "Leave" : $"Enter {TargetDisplayName}",
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
		if (playerInRange && !UiWindowManager.BlocksGameplay && @event.IsActionPressed("Interact"))
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
			GD.PushWarning($"Door '{Name}' used with no game in progress.");
			return;
		}
		if (ReturnsToPrevious)
		{
			if (string.IsNullOrEmpty(state.ReturnLevelPath))
			{
				GD.PushWarning($"Door '{Name}' has no stored level to return to.");
				return;
			}
			state.CurrentLevelPath = state.ReturnLevelPath;
			state.LocationName = state.ReturnLocationName;
			state.PlayerPosition = state.ReturnPosition;
			state.PlayerRotation = state.ReturnRotation;
			state.ReturnLevelPath = null;
			state.ReturnLocationName = null;
			state.ReturnPosition = null;
			state.ReturnRotation = null;
		}
		else
		{
			if (string.IsNullOrEmpty(TargetScenePath))
			{
				GD.PushWarning($"Door '{Name}' has no TargetScenePath to load.");
				return;
			}
			state.ReturnLevelPath = state.CurrentLevelPath;
			state.ReturnLocationName = state.LocationName;
			if (GetTree().GetFirstNodeInGroup(Player.GroupName) is Node3D player)
			{
				state.ReturnPosition = SaveManager.ToNumerics(player.GlobalPosition);
				state.ReturnRotation = SaveManager.ToNumerics(player.Rotation);
			}
			state.CurrentLevelPath = TargetScenePath;
			state.LocationName = TargetDisplayName;
			// Spawn at the interior's Spawn marker, not the stale outdoor
			// position.
			state.PlayerPosition = null;
			state.PlayerRotation = null;
		}
		LevelManager.Instance.StartLevel(state.CurrentLevelPath);
	}
}
