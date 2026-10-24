using System;
using System.Collections.Generic;
using System.Linq;
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
            GiverNpcId = "intro.dockmaster_hale",
            // The same two beats the markers below describe, as the journal's
            // "what am I doing right now" line. The second reaches itself: the
            // beat is holding the cube, so it is true however the party came by
            // it — including picking it up before ever meeting Hale.
            Stages =
            {
                QuestStage.Create(1, "Find the Maguffin Cube",
                    "The cube went missing somewhere on this deck. Search the station for it."),
                QuestStage.Create(2, "Return the cube to Dockmaster Hale",
                    "You have Hale's cube. He hasn't left his post by the dock.",
                    $"has_item:{ItemCatalog.MaguffinCubeId}"),
            },
            Reward = new QuestReward { Credits = 120, ExperiencePoints = 25 },
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
            GiverNpcId = "intro.chief_marlow",
            // Two ways to reach the second beat, which is the point of it
            // (quest-system.md Phase 4): putting Vex down reaches it by itself,
            // and buying him off reaches it by `set_stage` from his own
            // conversation. Marlow's turn-in reads the stage, so it accepts
            // either without knowing which happened.
            Stages =
            {
                QuestStage.Create(1, "Deal with Vex",
                    "Vex works the plaza. Marlow doesn't much mind how you go about it."),
                QuestStage.Create(2, "Report back to Chief Marlow",
                    "Vex is handled. Marlow owes you a maintenance keycard.",
                    "npc_defeated:intro.vex"),
            },
            // The keycard used to be a give_item in Marlow's turn-in line,
            // which the second route would have missed.
            Reward = new QuestReward
            {
                Credits = 40,
                ExperiencePoints = 15,
                Items = { QuestReward.Item(ItemCatalog.MaintenanceKeycardId) },
            },
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
        // After every Register, so a marker condition or a prerequisite may
        // name any quest — validating inside Register would read a half-filled
        // catalog and reject a forward reference.
        if (Problems(quests.Values) is { } problems)
        {
            throw new InvalidOperationException($"QuestCatalog has invalid quest definitions:{problems}");
        }
    }

    private static void Register(Quest quest)
    {
        quests.Add(quest.Id, quest);
    }

    // Everything wrong with a set of quest definitions, as one message, or null
    // when they are sound. A marker that names a missing item, a stage list
    // with a hole in it, a prerequisite naming a quest that doesn't exist —
    // each is a content bug that otherwise costs nothing until a player is
    // standing in it with no objective. Failing at first touch puts it in front
    // of the test suite instead.
    //
    // Takes the definitions rather than reading the registered catalog so tests
    // can check content the catalog would refuse, and so prerequisites resolve
    // against the set being validated.
    public static string Problems(IEnumerable<Quest> definitions)
    {
        var all = definitions?.ToList() ?? new List<Quest>();
        var ids = new HashSet<uint>(all.Select(quest => quest.Id));
        var problems = new StringBuilder();
        foreach (var quest in all)
        {
            foreach (var problem in QuestProblems(quest, ids))
            {
                problems.Append($"\n  quest {quest.Id} ({quest.Title}): {problem}");
            }
        }
        return problems.Length > 0 ? problems.ToString() : null;
    }

    private static IEnumerable<string> QuestProblems(Quest quest, HashSet<uint> knownIds)
    {
        foreach (var marker in quest.Markers ?? new List<QuestMarker>())
        {
            if (QuestMarker.Validate(marker) is { } problem)
            {
                yield return problem;
            }
        }
        foreach (var problem in StageProblems(quest))
        {
            yield return problem;
        }
        foreach (var problem in PrereqProblems(quest, knownIds))
        {
            yield return problem;
        }
        foreach (var problem in GiverProblems(quest))
        {
            yield return problem;
        }
        foreach (var problem in RewardProblems(quest))
        {
            yield return problem;
        }
    }

    // A giver is optional — plenty of quests will start from a trigger or
    // another quest — but a value that couldn't be an NpcId is a typo rather
    // than "nobody offers this", and it would show up only as an indicator
    // that never appears. Whether the named NPC exists is checked against the
    // .tres files on disk by the test suite, the same place a marker's npc:
    // target is: NpcDatabase is Godot-facing and this layer can't see it.
    private static IEnumerable<string> GiverProblems(Quest quest)
    {
        var giver = quest.GiverNpcId;
        if (string.IsNullOrEmpty(giver))
        {
            yield break;
        }
        if (string.IsNullOrWhiteSpace(giver))
        {
            yield return "giver npc id is blank";
        }
        else if (giver.IndexOf(QuestMarkerTarget.Separator) >= 0)
        {
            yield return $"giver npc id '{giver}' can't contain '{QuestMarkerTarget.Separator}'";
        }
    }

    // Stage numbers are 1..n in list order, with nothing missing and nothing
    // repeated: the journal shows "stage 2 of 3" and the stage verbs step
    // through the list, so a hole would be a beat the player can never sit on.
    // A quest with no stages at all is fine — that is every quest that shipped
    // before stages existed.
    private static IEnumerable<string> StageProblems(Quest quest)
    {
        var stages = quest.Stages ?? new List<QuestStage>();
        for (var i = 0; i < stages.Count; i++)
        {
            var expected = (uint)(i + 1);
            if (stages[i] == null)
            {
                yield return $"stage {expected} is null";
                continue;
            }
            if (stages[i].StageNumber != expected)
            {
                yield return $"stage numbers run 1..n in order; stage {i + 1} is numbered {stages[i].StageNumber}";
            }
            if (string.IsNullOrWhiteSpace(stages[i].SubtitleText))
            {
                yield return $"stage {stages[i].StageNumber} has no subtitle";
            }
            // A stage condition the vocabulary can't evaluate is a beat the
            // player could never reach, and it would fail silently: the watcher
            // treats an unevaluable condition as "not yet", forever.
            if (DialogueConditions.Validate(stages[i].ReachedWhen) is { } conditionProblem)
            {
                yield return $"stage {stages[i].StageNumber}: {conditionProblem}";
            }
        }
    }

    // A reward naming an item the catalog doesn't have, or handing over none of
    // it, is a payout the player silently never receives.
    private static IEnumerable<string> RewardProblems(Quest quest)
    {
        var reward = quest.Reward;
        if (reward == null)
        {
            yield break;
        }
        if (reward.IsEmpty)
        {
            yield return "reward grants nothing; leave it null instead";
        }
        foreach (var stack in reward.Items ?? new List<ItemStack>())
        {
            if (stack == null)
            {
                yield return "reward item is null";
            }
            else if (ItemCatalog.Get(stack.ItemId) == null)
            {
                yield return $"reward names item {stack.ItemId}, which doesn't exist";
            }
            else if (stack.Quantity == 0)
            {
                yield return $"reward grants 0 of item {stack.ItemId}";
            }
        }
    }

    // A prerequisite names another quest and the state it has to be in. It must
    // name a quest that exists and must not name its own, which would be a
    // quest that can never start.
    private static IEnumerable<string> PrereqProblems(Quest quest, HashSet<uint> knownIds)
    {
        var seen = new HashSet<uint>();
        foreach (var prereq in quest.PrereqQuests ?? new List<QuestPrereqFlag>())
        {
            if (prereq == null)
            {
                yield return "prerequisite is null";
                continue;
            }
            if (prereq.QuestId == quest.Id)
            {
                yield return "prerequisite names its own quest";
            }
            else if (!knownIds.Contains(prereq.QuestId))
            {
                yield return $"prerequisite names quest {prereq.QuestId}, which doesn't exist";
            }
            if (!seen.Add(prereq.QuestId))
            {
                yield return $"prerequisite names quest {prereq.QuestId} twice";
            }
        }
    }

    public static Quest Get(uint id) => quests.TryGetValue(id, out var quest) ? quest : null;

    public static IReadOnlyCollection<Quest> All => quests.Values;
}
