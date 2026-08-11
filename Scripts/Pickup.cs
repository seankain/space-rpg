using Godot;

// A collectible item in the world: an Area3D carrying an item id + quantity.
// When the player is inside the area, pressing Interact moves the item into
// the party inventory and removes the node from the scene.
public partial class Pickup : Area3D
{
	// Pickups in the loaded chunks join this group, so a quest marker can point
	// at an item where it is lying right now — and stop pointing at one that
	// has been collected, since the node is gone with it.
	public const string GroupName = "Pickup";

	[Export]
	public uint ItemId;

	[Export]
	public uint Quantity = 1;

	// Optional: collecting this moves QuestId to at least QuestStage — the
	// pickup half of QuestTrigger (quest-system.md Phase 2), for a fetch quest
	// whose next beat is "you have the thing". Set on the item's own scene, so
	// every copy of the cube carries the same meaning. 0 for either means the
	// pickup touches no quest.
	//
	// Idempotent like the trigger: a quest already at that stage or past it
	// doesn't move, so a second copy of the item changes nothing.
	[Export]
	public uint QuestId;

	[Export]
	public uint QuestStage;

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
		AddToGroup(GroupName);
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
		if (playerInRange && !UiWindowManager.BlocksGameplay && @event.IsActionPressed("Interact"))
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
		state.RecordEvent(GameEventKind.Item,
			Quantity > 1 ? $"Picked up {item.Name} x{Quantity}." : $"Picked up {item.Name}.",
			notify: true);
		// After the pickup line, so the log reads in the order it happened:
		// found the cube, then "take it back to Hale".
		if (QuestId != 0 && QuestStage != 0)
		{
			QuestManager.ReachStage(state, QuestId, QuestStage);
		}
		QueueFree();
	}
}
