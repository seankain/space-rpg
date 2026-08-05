using System.Collections.Generic;
using Xunit;

// quest-markers.md Phase 2 acceptance: `quest markers <id>` reports a quest's
// live objectives and where they resolve to, so marker content can be checked
// without a map. The locator is faked here — the real one (QuestTargetLocator)
// reads NpcDatabase and the baked manifests and is exercised in the running
// game.
public class QuestMarkerCommandTests
{
    private const uint FetchQuest = QuestCatalog.ReturnTheMaguffinId;

    private static IReadOnlyList<string> Args(params string[] a) => a;

    [Fact]
    public void ListsActiveMarkersWithTheirPositions()
    {
        var state = InProgress(FetchQuest);
        var locator = new FakeLocator
        {
            ["item:1"] = new QuestTargetLocation { AreaName = "IntroStation", WorldX = 4.5f, WorldZ = -8f },
        };

        var result = EditorQuestCommands.Run(state, Args("markers", FetchQuest.ToString()), locator);

        Assert.True(result.Success);
        Assert.Contains("Find the Maguffin Cube", result.Message);
        Assert.Contains("item:1", result.Message);
        Assert.Contains("4.5, -8", result.Message);
        Assert.Contains("IntroStation", result.Message);
    }

    [Fact]
    public void FollowsTheQuestAsItProgresses()
    {
        var state = InProgress(FetchQuest);
        state.Inventory.Add(ItemCatalog.MaguffinCubeId);

        var result = EditorQuestCommands.Run(state, Args("markers", FetchQuest.ToString()), new FakeLocator());

        Assert.Contains("Dockmaster Hale", result.Message);
        Assert.DoesNotContain("Find the Maguffin Cube", result.Message);
    }

    [Fact]
    public void SaysWhatItCannotPlaceInsteadOfListingNothing()
    {
        var state = InProgress(FetchQuest);

        var result = EditorQuestCommands.Run(state, Args("markers", FetchQuest.ToString()), new FakeLocator());

        Assert.True(result.Success);
        Assert.Contains("Find the Maguffin Cube", result.Message);
        Assert.Contains("not locatable here", result.Message);
    }

    [Fact]
    public void SaysWhenThereIsNoRunningLevelToLocateAgainst()
    {
        var state = InProgress(FetchQuest);

        var result = EditorQuestCommands.Run(state, Args("markers", FetchQuest.ToString()));

        Assert.True(result.Success);
        Assert.Contains("no locator", result.Message);
    }

    [Fact]
    public void ReportsAQuestThatIsNotInProgress()
    {
        var state = new GameState();

        var result = EditorQuestCommands.Run(state, Args("markers", FetchQuest.ToString()), new FakeLocator());

        Assert.True(result.Success);
        Assert.Contains("Unstarted", result.Message);
        Assert.Contains("only an in-progress quest has markers", result.Message);
    }

    [Fact]
    public void RejectsAnUnknownQuestId()
    {
        Assert.False(EditorQuestCommands.Run(new GameState(), Args("markers", "99999")).Success);
        Assert.False(EditorQuestCommands.Run(new GameState(), Args("markers")).Success);
    }

    private static GameState InProgress(uint questId)
    {
        var state = new GameState();
        state.SetQuestState(questId, QUESTSUCCESSSTATE.InProgress);
        return state;
    }

    private sealed class FakeLocator : IQuestTargetLocator
    {
        private readonly Dictionary<string, QuestTargetLocation> locations = new();

        public QuestTargetLocation this[string token]
        {
            set => locations[token] = value;
        }

        public bool TryLocate(QuestMarkerTarget target, out QuestTargetLocation location) =>
            locations.TryGetValue(target.ToToken(), out location);
    }
}
