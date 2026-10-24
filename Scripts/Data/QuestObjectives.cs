using System;
using System.Collections.Generic;
using System.Linq;

// "What am I doing right now", in one line (quest-system.md Phase 3). The
// journal has room to show a quest's stage *and* its markers; the HUD has one
// line over the world, so this picks between them.
//
// Engine-free, so the choice is testable and the HUD stays a label: the same
// question with a different amount of room to answer it is exactly the sort of
// thing that ends up worded three ways across three scenes otherwise.
public static class QuestObjectives
{
    // Between two objectives with equal claim on the line — a quest with no
    // stages whose markers both apply.
    public const string Separator = "  •  ";

    // The objective text for a quest in progress, or null when there is
    // nothing to say (a quest not being played, or one with neither a stage
    // nor an applicable marker).
    //
    // The stage wins when the quest has one: it is the authored "what you are
    // doing", while a marker says "where to go", and a quest with several live
    // markers has no single one to show. A quest with no stages falls back to
    // its markers, which is what every quest looked like before stages.
    public static string CurrentFor(GameState state, uint questId, Action<string> warn = null) =>
        CurrentFor(state, QuestCatalog.Get(questId), warn);

    // The same for a caller holding the definition, and the way a test asks
    // about content the catalog doesn't carry — the overload pair
    // QuestMarkerResolver established.
    public static string CurrentFor(GameState state, Quest quest, Action<string> warn = null)
    {
        if (state == null || quest == null
            || state.GetQuestState(quest.Id) != QUESTSUCCESSSTATE.InProgress)
        {
            return null;
        }
        if (quest.GetStage(state.GetQuestStage(quest.Id))?.SubtitleText is { } subtitle
            && !string.IsNullOrWhiteSpace(subtitle))
        {
            return subtitle;
        }
        var labels = QuestMarkerResolver.ActiveMarkers(state, quest, warn)
            .Select(marker => marker.Label)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .ToList();
        return labels.Count > 0 ? string.Join(Separator, labels) : null;
    }

    // The same for whichever quest the player is following, with its title, as
    // the HUD shows it: null when nothing is tracked, else the title and — when
    // there is one — the objective under it.
    public static QuestHeadline Tracked(GameState state, Action<string> warn = null)
    {
        var questId = state?.TrackedQuestId ?? 0;
        var quest = questId == 0 ? null : QuestCatalog.Get(questId);
        if (quest == null || state.GetQuestState(questId) != QUESTSUCCESSSTATE.InProgress)
        {
            return null;
        }
        return new QuestHeadline
        {
            QuestId = questId,
            Title = quest.Title,
            Objective = CurrentFor(state, questId, warn) ?? "",
        };
    }
}

// The tracked quest as the HUD draws it: a title and the line under it.
public class QuestHeadline
{
    public uint QuestId { get; set; }

    public string Title { get; set; } = "";

    // Empty when the quest has nothing to point at right now, which the HUD
    // renders as the title alone rather than as a blank second line.
    public string Objective { get; set; } = "";
}
