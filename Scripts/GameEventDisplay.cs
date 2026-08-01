using Godot;

// How a GameEventKind reads on screen, shared by the Log tab and the toast
// layer so a credit line is the same colour wherever it shows up. Godot-side
// on purpose: GameEvent itself stays engine-free.
public static class GameEventDisplay
{
	// Ordered as the Log tab's filter dropdown lists them, after its
	// leading "Everything" entry.
	public static readonly GameEventKind[] Kinds =
	{
		GameEventKind.Item,
		GameEventKind.Credits,
		GameEventKind.Battle,
		GameEventKind.Conversation,
		GameEventKind.Quest,
		GameEventKind.Party,
	};

	// The dropdown label and the "[Item]" prefix on a log row.
	public static string LabelOf(GameEventKind kind) => kind switch
	{
		GameEventKind.Item => "Items",
		GameEventKind.Credits => "Credits",
		GameEventKind.Battle => "Battles",
		GameEventKind.Conversation => "Conversations",
		GameEventKind.Quest => "Quests",
		GameEventKind.Party => "Party",
		_ => kind.ToString(),
	};

	// Bright enough to read as a toast over an arbitrary 3D background and
	// against the menu's dark list rows alike.
	public static Color ColorOf(GameEventKind kind) => kind switch
	{
		GameEventKind.Item => new Color(0.62f, 0.86f, 1f),
		GameEventKind.Credits => new Color(1f, 0.85f, 0.42f),
		GameEventKind.Battle => new Color(1f, 0.55f, 0.5f),
		GameEventKind.Conversation => new Color(0.78f, 0.78f, 0.82f),
		GameEventKind.Quest => new Color(0.65f, 1f, 0.7f),
		GameEventKind.Party => new Color(0.85f, 0.7f, 1f),
		_ => Colors.White,
	};
}
