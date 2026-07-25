using System.Collections.Generic;
using System.Linq;

// The `dialogue` console verb (dialogue-editor plan Phase 3): opens the
// read-only dialogue viewer and lists known conversations. Godot-facing (it
// drives DevConsole's viewer and reads DialogueCatalog), so it lives here
// rather than in the engine-free Commands/ folder. Phase 4 adds the editing
// subcommands (new/save/assign) alongside these.
public sealed class DialogueCommand : IConsoleCommand
{
	private readonly DevConsole console;

	public DialogueCommand(DevConsole console)
	{
		this.console = console;
	}

	public string Name => "dialogue";
	public string Usage => "dialogue <list|open|close> [id]";
	public string Summary => "List conversations, or open one in the read-only viewer.";

	public CommandResult Execute(IReadOnlyList<string> args)
	{
		var sub = args.Count > 0 ? args[0].ToLowerInvariant() : "";
		return sub switch
		{
			"list" => List(),
			"open" => Open(args),
			"close" => Close(),
			_ => CommandResult.Fail($"Usage: {Usage}"),
		};
	}

	private static CommandResult List()
	{
		var ids = DialogueCatalog.All
			.OrderBy(g => g.Id, System.StringComparer.OrdinalIgnoreCase)
			.Select(g => $"  {g.Id}  ({g.Nodes.Count} nodes)")
			.ToList();
		return ids.Count == 0
			? CommandResult.Ok("No dialogue files under Resources/Dialogue.")
			: CommandResult.Ok($"Dialogues ({ids.Count}):\n" + string.Join("\n", ids));
	}

	private CommandResult Open(IReadOnlyList<string> args)
	{
		if (args.Count < 2)
		{
			return CommandResult.Fail("Usage: dialogue open <id>");
		}
		return console.OpenDialogueViewer(args[1]);
	}

	private CommandResult Close()
	{
		return console.CloseDialogueViewer()
			? CommandResult.Ok("Closed dialogue viewer.")
			: CommandResult.Ok("Dialogue viewer is not open.");
	}
}
