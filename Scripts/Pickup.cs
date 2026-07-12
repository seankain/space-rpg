using Godot;

// A collectible item in the world: an Area3D carrying an item id + quantity.
// When the player is inside the area, pressing Interact moves the item into
// the party inventory and removes the node from the scene.
public partial class Pickup : Area3D
{
	[Export]
	public uint ItemId;

	[Export]
	public uint Quantity = 1;

	// Idle spin so pickups read as collectible; 0 to disable.
	[Export]
	public float SpinRadiansPerSecond = 1.0f;

	// Height above the pickup's origin where the interaction hint floats.
	[Export]
	public float PromptHeight = 0.75f;

	private bool playerInRange;
	private InteractionPrompt prompt;

	public override void _Ready()
	{
		var item = ItemCatalog.Get(ItemId);
		prompt = new InteractionPrompt
		{
			ActionName = "Interact",
			ActionDescription = item != null ? $"Pick up {item.Name}" : "Pick up",
			Position = new Vector3(0, PromptHeight, 0),
			Visible = false,
		};
		AddChild(prompt);

		BodyEntered += body =>
		{
			if (body.IsInGroup(Player.GroupName))
			{
				playerInRange = true;
				prompt.Visible = true;
			}
		};
		BodyExited += body =>
		{
			if (body.IsInGroup(Player.GroupName))
			{
				playerInRange = false;
				prompt.Visible = false;
			}
		};
	}

	public override void _Process(double delta)
	{
		if (SpinRadiansPerSecond != 0.0f)
		{
			RotateY(SpinRadiansPerSecond * (float)delta);
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (playerInRange && @event.IsActionPressed("Interact"))
		{
			GetViewport().SetInputAsHandled();
			Collect();
		}
	}

	private void Collect()
	{
		var state = SaveManager.Instance?.CurrentState;
		var item = ItemCatalog.Get(ItemId);
		if (state == null || item == null)
		{
			GD.PushWarning($"Pickup '{Name}' could not be collected (no game state or unknown item id {ItemId}).");
			return;
		}
		state.Inventory.Add(ItemId, Quantity);
		// TODO: replace with a HUD toast once PlayerHud grows one.
		GD.Print($"Picked up {item.Name} x{Quantity}");
		QueueFree();
	}
}
