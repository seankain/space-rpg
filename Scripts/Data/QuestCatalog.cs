using System;
using System.Collections.Generic;
using System.Text;

// All quest definitions, keyed by Quest.Id — the same pattern as ItemCatalog:
// saves and runtime state track progress by quest id only (GameState.Quests),
// so definitions can be reworded freely. Ids are permanent once shipped.
public static class QuestCatalog
{
    public const uint ReturnTheMaguffinId = 1;
    public const uint ClearTheDeckId = 2;

    private static readonly Dictionary<uint, Quest> quests = new();

    static QuestCatalog()
    {
        Register(new Quest
        {
            Id = ReturnTheMaguffinId,
            Title = "Return the Maguffin",
            Description = "Dockmaster Hale lost his prized Maguffin Cube somewhere on the station. Find it and bring it back to him.",
            SideQuest = false,
            PrereqQuests = new List<QuestPrereqFlag>(),
            Markers =
            {
                // Two halves of one fetch: point at the cube until the party
                // has it, then at the man who wants it back.
                QuestMarker.Create(
                    QuestMarkerTarget.Item(ItemCatalog.MaguffinCubeId),
                    "Find the Maguffin Cube",
                    $"!has_item:{ItemCatalog.MaguffinCubeId}"),
                QuestMarker.Create(
                    QuestMarkerTarget.Npc("intro.dockmaster_hale"),
                    "Return the cube to Dockmaster Hale",
                    $"has_item:{ItemCatalog.MaguffinCubeId}"),
            },
        });
        Register(new Quest
        {
            Id = ClearTheDeckId,
            Title = "Clear the Deck",
            Description = "Chief Marlow wants Vex, the thug shaking down couriers on the plaza, taught a lesson. Defeat him and report back for a maintenance keycard.",
            SideQuest = true,
            PrereqQuests = new List<QuestPrereqFlag>(),
            Markers =
            {
                QuestMarker.Create(
                    QuestMarkerTarget.Npc("intro.vex"),
                    "Deal with Vex",
                    "!npc_defeated:intro.vex"),
                QuestMarker.Create(
                    QuestMarkerTarget.Npc("intro.chief_marlow"),
                    "Report back to Chief Marlow",
                    "npc_defeated:intro.vex"),
            },
        });
        // After every Register, so a marker condition may name any quest —
        // validating inside Register would read a half-filled catalog and
        // reject a forward reference.
        ValidateMarkers();
    }

    private static void Register(Quest quest)
    {
        quests.Add(quest.Id, quest);
    }

    // A marker that names a missing item or a condition the vocabulary can't
    // evaluate is a content bug: fail loudly at first touch (which the test
    // suite reaches long before a play session does) instead of quietly
    // leaving the player with no objective on the map.
    private static void ValidateMarkers()
    {
        var problems = new StringBuilder();
        foreach (var quest in quests.Values)
        {
            foreach (var marker in quest.Markers ?? new List<QuestMarker>())
            {
                if (QuestMarker.Validate(marker) is { } problem)
                {
                    problems.Append($"\n  quest {quest.Id} ({quest.Title}): {problem}");
                }
            }
        }
        if (problems.Length > 0)
        {
            throw new InvalidOperationException($"QuestCatalog has invalid quest markers:{problems}");
        }
    }

    public static Quest Get(uint id) => quests.TryGetValue(id, out var quest) ? quest : null;

    public static IReadOnlyCollection<Quest> All => quests.Values;
}
