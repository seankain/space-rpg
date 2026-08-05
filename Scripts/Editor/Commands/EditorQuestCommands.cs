using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

// Engine-free logic behind the quest console verbs (in-game-editor plan
// Phase 5). Like the item commands, these mutate the running GameState that the
// save system persists, so a Save Game keeps editor quest changes. Engine-free
// (GameState, QuestCatalog, QUESTSUCCESSSTATE are all engine-free) so the
// commands are unit-tested against a fabricated GameState.
public static class EditorQuestCommands
{
    // quest <start|set|stage|advance|markers> <questId> [state|n]
    //
    // `locator` is the Godot-side answer to "where is this target?"
    // (QuestTargetLocator), passed in by the console wrapper so this stays
    // engine-free; without one, `markers` still lists objectives and says it
    // has no positions.
    public static CommandResult Run(
        GameState state, IReadOnlyList<string> args, IQuestTargetLocator locator = null)
    {
        if (state == null)
        {
            return NoGame();
        }
        if (args.Count < 1)
        {
            return Usage();
        }
        return args[0].ToLowerInvariant() switch
        {
            "start" => Start(state, args),
            "set" => Set(state, args),
            "stage" => Stage(state, args),
            "advance" => Advance(state, args),
            "markers" => Markers(state, args, locator),
            _ => CommandResult.Fail($"Unknown quest subcommand '{args[0]}'. {UsageText}"),
        };
    }

    // quests: list every catalog quest with its current state and stage.
    public static CommandResult List(GameState state)
    {
        if (state == null)
        {
            return NoGame();
        }
        var builder = new StringBuilder("Quests:");
        foreach (var quest in QuestCatalog.All.OrderBy(q => q.Id))
        {
            var progress = state.Quests.FirstOrDefault(p => p.QuestId == quest.Id);
            var questState = progress?.State ?? QUESTSUCCESSSTATE.Unstarted;
            var stage = progress?.CurrentStageNumber ?? 0;
            builder.Append('\n').Append("  ").Append(quest.Id).Append("  ")
                .Append(quest.Title).Append("  [").Append(quest.SideQuest ? "side" : "main").Append("]  ")
                .Append(questState).Append(", stage ").Append(stage);
        }
        return CommandResult.Ok(builder.ToString());
    }

    private static CommandResult Start(GameState state, IReadOnlyList<string> args)
    {
        if (!TryQuest(args, 1, out var id, out var error))
        {
            return CommandResult.Fail(error);
        }
        state.SetQuestState(id, QUESTSUCCESSSTATE.InProgress);
        return CommandResult.Ok($"Started quest {id}: {QuestCatalog.Get(id).Title} (InProgress).");
    }

    private static CommandResult Set(GameState state, IReadOnlyList<string> args)
    {
        if (!TryQuest(args, 1, out var id, out var error))
        {
            return CommandResult.Fail(error);
        }
        if (args.Count < 3 || !TryParseState(args[2], out var target))
        {
            return CommandResult.Fail("Usage: quest set <questId> <unstarted|inprogress|success|failed>");
        }
        state.SetQuestState(id, target);
        return CommandResult.Ok($"Quest {id} ({QuestCatalog.Get(id).Title}) set to {target}.");
    }

    private static CommandResult Stage(GameState state, IReadOnlyList<string> args)
    {
        if (!TryQuest(args, 1, out var id, out var error))
        {
            return CommandResult.Fail(error);
        }
        if (args.Count < 3 || !uint.TryParse(args[2], out var stage))
        {
            return CommandResult.Fail("Usage: quest stage <questId> <stageNumber>");
        }
        // Deliberately unvalidated against a stage list: no quest declares
        // QuestStages yet. Follow-up in quest-system.md is to bound n once they do.
        state.GetOrAddQuestProgress(id).CurrentStageNumber = stage;
        return CommandResult.Ok($"Quest {id} ({QuestCatalog.Get(id).Title}) stage set to {stage}.");
    }

    private static CommandResult Advance(GameState state, IReadOnlyList<string> args)
    {
        if (!TryQuest(args, 1, out var id, out var error))
        {
            return CommandResult.Fail(error);
        }
        var progress = state.GetOrAddQuestProgress(id);
        progress.CurrentStageNumber += 1;
        return CommandResult.Ok($"Quest {id} ({QuestCatalog.Get(id).Title}) advanced to stage {progress.CurrentStageNumber}.");
    }

    // quest markers <questId>: which of a quest's markers apply right now and
    // where they resolve to — the way to check marker content without a map
    // (quest-markers.md Phase 2). Reports what it can't place, since a silent
    // gap is exactly the bug this verb exists to find.
    private static CommandResult Markers(
        GameState state, IReadOnlyList<string> args, IQuestTargetLocator locator)
    {
        if (!TryQuest(args, 1, out var id, out var error))
        {
            return CommandResult.Fail(error);
        }
        var quest = QuestCatalog.Get(id);
        var problems = new List<string>();
        var active = QuestMarkerResolver.ActiveMarkers(state, quest, problems.Add);
        if (active.Count == 0)
        {
            var questState = state.GetQuestState(id);
            return CommandResult.Ok(questState == QUESTSUCCESSSTATE.InProgress
                ? $"Quest {id} ({quest.Title}) has no active markers right now."
                : $"Quest {id} ({quest.Title}) is {questState}; only an in-progress quest has markers.");
        }
        var builder = new StringBuilder($"Markers for quest {id} ({quest.Title}):");
        foreach (var marker in active)
        {
            builder.Append("\n  ").Append(marker.Label).Append("  [").Append(marker.Target.ToToken()).Append(']');
            if (locator != null && locator.TryLocate(marker.Target, out var location) && location != null)
            {
                builder.Append("  at ")
                    .Append(location.WorldX.ToString("0.##", CultureInfo.InvariantCulture)).Append(", ")
                    .Append(location.WorldZ.ToString("0.##", CultureInfo.InvariantCulture));
                var where = !string.IsNullOrEmpty(location.AreaName) ? location.AreaName : location.ScenePath;
                if (!string.IsNullOrEmpty(where))
                {
                    builder.Append("  in ").Append(where);
                }
            }
            else
            {
                builder.Append(locator == null ? "  (no locator: not in a running level)" : "  (not locatable here)");
            }
        }
        foreach (var problem in problems)
        {
            builder.Append("\n  ! ").Append(problem);
        }
        return CommandResult.Ok(builder.ToString());
    }

    private static bool TryQuest(IReadOnlyList<string> args, int idIndex, out uint id, out string error)
    {
        error = null;
        id = 0;
        if (args.Count <= idIndex || !uint.TryParse(args[idIndex], out id))
        {
            error = "Expected a numeric quest id.";
            return false;
        }
        if (QuestCatalog.Get(id) == null)
        {
            error = $"Unknown quest id {id}. Try 'quests'.";
            return false;
        }
        return true;
    }

    private static bool TryParseState(string token, out QUESTSUCCESSSTATE state) =>
        Enum.TryParse(token, ignoreCase: true, out state)
        && Enum.IsDefined(typeof(QUESTSUCCESSSTATE), state);

    private const string UsageText = "Usage: quest <start|set|stage|advance|markers> <questId> [state|n]";

    private static CommandResult Usage() => CommandResult.Fail(UsageText);

    private static CommandResult NoGame() =>
        CommandResult.Fail("No game in progress. Start or load a game first.");
}
