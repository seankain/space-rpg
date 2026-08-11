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
    // "advance" moves the success state and "stage" moves the stage, in both
    // this vocabulary and the dialogue one (advance_quest / set_stage): the two
    // words meant different things in the two places until stages landed
    // (quest-system.md Phase 1).
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
            "track" => Track(state, args),
            "untrack" => Untrack(state),
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
            builder.Append('\n')
                // A leading marker for the tracked quest, so the list answers
                // "which one am I following?" without a second verb.
                .Append(quest.Id == state.TrackedQuestId ? "> " : "  ")
                .Append(quest.Id).Append("  ")
                .Append(quest.Title).Append("  [").Append(quest.SideQuest ? "side" : "main").Append("]  ")
                .Append(questState);
            // Stages are optional, so say nothing about them for a quest that
            // declares none rather than reporting a meaningless "stage 0".
            if (quest.HasStages)
            {
                builder.Append(", stage ").Append(stage).Append(" of ").Append(quest.Stages.Count);
                if (quest.GetStage(stage) is { } current)
                {
                    builder.Append(": ").Append(current.SubtitleText);
                }
            }
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

    // quest stage <questId> <n|next>: jump to a declared stage, or step to the
    // one after the current stage. Bounded by the definition — a stage the
    // quest doesn't declare is a typo, and writing it would leave the journal
    // showing a beat with no text.
    private static CommandResult Stage(GameState state, IReadOnlyList<string> args)
    {
        if (!TryQuest(args, 1, out var id, out var error))
        {
            return CommandResult.Fail(error);
        }
        var quest = QuestCatalog.Get(id);
        if (args.Count < 3)
        {
            return CommandResult.Fail($"Usage: quest stage <questId> <stageNumber|{DialogueEffects.NextStageArg}>");
        }
        if (!quest.HasStages)
        {
            return CommandResult.Fail($"Quest {id} ({quest.Title}) declares no stages.");
        }
        var raw = args[2];
        uint target;
        if (string.Equals(raw, DialogueEffects.NextStageArg, StringComparison.OrdinalIgnoreCase))
        {
            target = quest.NextStageNumber(state.GetQuestStage(id));
            if (target == 0)
            {
                return CommandResult.Fail(
                    $"Quest {id} ({quest.Title}) is already on its last stage "
                    + $"({state.GetQuestStage(id)} of {quest.Stages.Count}).");
            }
        }
        else if (!uint.TryParse(raw, out target))
        {
            return CommandResult.Fail(
                $"Usage: quest stage <questId> <stageNumber|{DialogueEffects.NextStageArg}>");
        }
        if (!state.SetQuestStage(id, target))
        {
            return CommandResult.Fail(
                $"Quest {id} ({quest.Title}) has no stage {target}; it has {quest.Stages.Count}.");
        }
        return CommandResult.Ok(
            $"Quest {id} ({quest.Title}) stage set to {target} of {quest.Stages.Count}: "
            + $"{quest.GetStage(target).SubtitleText}");
    }

    // quest advance <questId>: one step along Unstarted -> InProgress ->
    // Success, the console twin of the dialogue vocabulary's advance_quest.
    // Stages move with `quest stage`, not with this.
    private static CommandResult Advance(GameState state, IReadOnlyList<string> args)
    {
        if (!TryQuest(args, 1, out var id, out var error))
        {
            return CommandResult.Fail(error);
        }
        var current = state.GetQuestState(id);
        var next = current switch
        {
            QUESTSUCCESSSTATE.Unstarted => QUESTSUCCESSSTATE.InProgress,
            QUESTSUCCESSSTATE.InProgress => QUESTSUCCESSSTATE.Success,
            _ => current,
        };
        if (next == current)
        {
            return CommandResult.Fail(
                $"Quest {id} ({QuestCatalog.Get(id).Title}) is {current}; there is nowhere further to advance it.");
        }
        state.SetQuestState(id, next);
        return CommandResult.Ok($"Quest {id} ({QuestCatalog.Get(id).Title}) advanced to {next}.");
    }

    // quest track <questId>: follow a quest, the console equivalent of picking
    // it in the journal — the map draws the tracked quest's markers.
    private static CommandResult Track(GameState state, IReadOnlyList<string> args)
    {
        if (!TryQuest(args, 1, out var id, out var error))
        {
            return CommandResult.Fail(error);
        }
        if (!state.SetTrackedQuest(id))
        {
            return CommandResult.Fail(
                $"Quest {id} ({QuestCatalog.Get(id).Title}) is {state.GetQuestState(id)}; "
                + "only an in-progress quest can be tracked.");
        }
        return CommandResult.Ok($"Now tracking quest {id}: {QuestCatalog.Get(id).Title}.");
    }

    private static CommandResult Untrack(GameState state)
    {
        state.SetTrackedQuest(0);
        return CommandResult.Ok("No quest tracked.");
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

    private const string UsageText =
        "Usage: quest <start|set|stage|advance|markers|track> <questId> [state|n|next], or quest untrack";

    private static CommandResult Usage() => CommandResult.Fail(UsageText);

    private static CommandResult NoGame() =>
        CommandResult.Fail("No game in progress. Start or load a game first.");
}
