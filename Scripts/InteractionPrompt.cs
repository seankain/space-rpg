using Godot;

// World-space UI hint floated near an interactable object: a billboarded
// label reading e.g. "[E] Pick up Maguffin Cube". The key glyph is resolved
// from the InputMap at runtime, so rebinding the action updates the hint
// without touching callers. Callers control visibility.
public partial class InteractionPrompt : Label3D
{
	// InputMap action whose binding is shown in brackets.
	[Export]
	public string ActionName = "Interact";

	// What pressing the button does, e.g. "Pick up Maguffin Cube" or "Talk".
	[Export]
	public string ActionDescription = "Interact";

	public override void _Ready()
	{
		Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
		// Keep a constant on-screen size and draw over geometry so the hint
		// stays readable regardless of camera distance or occlusion.
		FixedSize = true;
		NoDepthTest = true;
		RenderPriority = 10;
		PixelSize = 0.0008f;
		FontSize = 40;
		OutlineSize = 10;
		OutlineModulate = new Color(0, 0, 0, 0.85f);
		RefreshText();
	}

	public void SetAction(string actionName, string actionDescription)
	{
		ActionName = actionName;
		ActionDescription = actionDescription;
		RefreshText();
	}

	private void RefreshText()
	{
		Text = $"[{FirstBindingLabel(ActionName)}] {ActionDescription}";
	}

	// Shared with other UI that shows key hints (e.g. the dialogue box).
	public static string FirstBindingLabel(string actionName)
	{
		if (!InputMap.HasAction(actionName))
		{
			GD.PushWarning($"InteractionPrompt: unknown input action '{actionName}'.");
			return actionName;
		}
		foreach (var inputEvent in InputMap.ActionGetEvents(actionName))
		{
			switch (inputEvent)
			{
				case InputEventKey key:
					// Bindings may use either a logical keycode or a physical
					// one; map physical through the active keyboard layout.
					var keycode = key.Keycode != Key.None
						? key.Keycode
						: DisplayServer.KeyboardGetKeycodeFromPhysical(key.PhysicalKeycode);
					var label = OS.GetKeycodeString(keycode);
					if (!string.IsNullOrEmpty(label))
					{
						return label;
					}
					break;
				case InputEventJoypadButton joypadButton:
					return $"Joy {(int)joypadButton.ButtonIndex}";
				case InputEventMouseButton mouseButton:
					return $"Mouse {(int)mouseButton.ButtonIndex}";
			}
		}
		return actionName;
	}
}
