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
	public string Usage => "dialogue <list|open|new|save|play|close|assign> [args]";
	public string Summary => "List, open, edit, save, preview, or assign conversations.";

	public CommandResult Execute(IReadOnlyList<string> args)
	{
		var sub = args.Count > 0 ? args[0].ToLowerInvariant() : "";
		return sub switch
		{
			"list" => List(),
			"open" => Open(args),
			"new" => New(args),
			"save" => Save(args),
			"play" => Play(args),
			"close" => Close(),
			"assign" => Assign(args),
			_ => CommandResult.Fail($"Usage: {Usage}"),
		};
	}

	private static CommandResult List()
	{
		// The format is listed because that is what 'dialogue save' writes back
		// (npc-dialogue-yarn.md Phase 1).
		var ids = DialogueCatalog.All
			.OrderBy(g => g.Id, System.StringComparer.OrdinalIgnoreCase)
			.Select(g => $"  {g.Id}  ({g.Nodes.Count} nodes, {g.SourceFormat.ToString().ToLowerInvariant()})")
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

	private CommandResult New(IReadOnlyList<string> args)
	{
		if (args.Count < 2)
		{
			return CommandResult.Fail("Usage: dialogue new <id>");
		}
		return console.NewDialogue(args[1]);
	}

	// dialogue save [id] — save the open graph, optionally under a new id.
	private CommandResult Save(IReadOnlyList<string> args)
	{
		return console.SaveDialogue(args.Count > 1 ? args[1] : null);
	}

	// dialogue play [nodeId] — preview the open graph from a node (default: the
	// node selected in the editor, else the entry).
	private CommandResult Play(IReadOnlyList<string> args)
	{
		return console.PlayDialoguePreview(args.Count > 1 ? args[1] : null);
	}

	private CommandResult Close()
	{
		return console.CloseDialogueViewer()
			? CommandResult.Ok("Closed dialogue editor.")
			: CommandResult.Ok("Dialogue editor is not open.");
	}

	private static CommandResult Assign(IReadOnlyList<string> args)
	{
		if (args.Count < 3)
		{
			return CommandResult.Fail("Usage: dialogue assign <npcId> <dialogueId>");
		}
		return DialogueEditing.Assign(args[1], args[2]);
	}
}
