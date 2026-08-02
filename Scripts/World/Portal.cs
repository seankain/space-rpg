using Godot;

// A portal between top-level game areas (root scenes like Intro.tscn and
// World1.tscn). It works like a Door: pressing Interact in range swaps the
// running level through LevelManager.StartLevel, and the destination's own
// ChunkManager streams in that area's chunks. Unlike a Door, portal travel is
// one-way area movement, not an interior visit: nothing is recorded in
// GameState.Return*, and the player arrives at the destination level's Spawn
// marker.
//
// A portal can be gated behind a quest item (RequiredItemId): until the party
// inventory holds it, the surface is dimmed and the prompt names the missing
// item instead of offering travel.
public partial class Portal : Node3D
{
	[Export(PropertyHint.File, "*.tscn")]
	public string TargetScenePath;

	// Shown in the prompt and used as the destination's LocationName.
	[Export]
	public string TargetDisplayName = "Unknown";

	// ItemCatalog id the party inventory must hold before the portal
	// activates; 0 means always active. Lock state is re-read from the
	// inventory whenever the player steps into range or presses Interact, so
	// no unlock event is needed.
	[Export]
	public uint RequiredItemId;

	// The glowing portal surface, dimmed while the portal is locked.
	[Export]
	public MeshInstance3D Surface;

	[Export]
	public float InteractRadius = 2.0f;

	// Height above the portal's origin where the interaction hint floats.
	[Export]
	public float PromptHeight = 2.2f;

	private bool playerInRange;
	private InteractionPrompt prompt;
	private Material activeSurfaceMaterial;
	private Material lockedSurfaceMaterial;

	public override void _Ready()
	{
		prompt = new InteractionPrompt
		{
			Position = new Vector3(0, PromptHeight, 0),
			Visible = false,
		};
		AddChild(prompt);

		if (Surface != null)
		{
			activeSurfaceMaterial = Surface.GetSurfaceOverrideMaterial(0);
			if (activeSurfaceMaterial is StandardMaterial3D active)
			{
				// Duplicated so dimming one portal never touches the material
				// shared by other Portal instances.
				var locked = (StandardMaterial3D)active.Duplicate();
				locked.AlbedoColor = new Color(0.22f, 0.27f, 0.22f);
				lockedSurfaceMaterial = locked;
			}
		}
		Refresh();

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
				Refresh();
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
			// Live check, not the state the prompt was drawn with: the item
			// may have been acquired without leaving the zone.
			Refresh();
			if (!IsLocked())
			{
				Travel();
			}
		}
	}

	private bool IsLocked() =>
		RequiredItemId != 0
		&& (SaveManager.Instance?.CurrentState?.Inventory.CountOf(RequiredItemId) ?? 0) == 0;

	// Points the prompt and surface at the current lock state.
	private void Refresh()
	{
		if (IsLocked())
		{
			var item = ItemCatalog.Get(RequiredItemId);
			prompt.SetAction("", $"Requires {item?.Name ?? $"item {RequiredItemId}"}");
		}
		else
		{
			prompt.SetAction("Interact", $"Travel to {TargetDisplayName}");
		}
		Surface?.SetSurfaceOverrideMaterial(0, IsLocked()
			? lockedSurfaceMaterial ?? activeSurfaceMaterial
			: activeSurfaceMaterial);
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
