using System.Collections.Generic;

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
        });
        Register(new Quest
        {
            Id = ClearTheDeckId,
            Title = "Clear the Deck",
            Description = "Chief Marlow wants Vex, the thug shaking down couriers on the plaza, taught a lesson. Defeat him and report back for a maintenance keycard.",
            SideQuest = true,
            PrereqQuests = new List<QuestPrereqFlag>(),
        });
    }

    private static void Register(Quest quest)
    {
        quests.Add(quest.Id, quest);
    }

    public static Quest Get(uint id) => quests.TryGetValue(id, out var quest) ? quest : null;

    public static IReadOnlyCollection<Quest> All => quests.Values;
}
