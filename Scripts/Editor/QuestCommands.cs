using System.Collections.Generic;

// The quest console verbs (in-game-editor plan Phase 5), thin wrappers that hand
// the running GameState to the engine-free EditorQuestCommands. Godot-facing
// (they read SaveManager), so they live here rather than in the engine-free
// Commands/ folder.
//
// Feedback is printed to the console; an already-open Quest tab won't
// live-update, since QuestLogMenu only refreshes when the tab becomes visible.

public sealed class QuestCommand : IConsoleCommand
{
    public string Name => "quest";
    public string Usage => "quest <start|set|stage|advance|markers> <questId> [state|n]";
    public string Summary => "Start, set the state of, advance, or show the markers of a quest.";

    // The locator is built per call: it answers against whichever level is
    // running, and there may be none (title screen) — `markers` then says so
    // rather than pretending a target is missing.
    public CommandResult Execute(IReadOnlyList<string> args) =>
        EditorQuestCommands.Run(
            SaveManager.Instance?.CurrentState, args, QuestTargetLocator.ForCurrentLevel());
}

public sealed class QuestsCommand : IConsoleCommand
{
    public string Name => "quests";
    public string Usage => "quests";
    public string Summary => "List quests with their current state and stage.";

    public CommandResult Execute(IReadOnlyList<string> args) =>
        EditorQuestCommands.List(SaveManager.Instance?.CurrentState);
}
