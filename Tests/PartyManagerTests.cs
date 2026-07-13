using System.Collections.Generic;
using System.Linq;
using Xunit;

// Party plan Phase 1 acceptance: add/remove/leader rules on the roster.
public class PartyManagerTests
{
    private static CharacterEntity Member(ulong id) => new()
    {
        Id = id,
        Name = $"Member {id}",
        Level = 1,
        HealthPoints = 10,
        MaxHealthPoints = 10,
        Stats = new CharacterStats(),
        EquipSlots = new CharacterEquipSlots(),
        ActiveStatusEffects = new List<ActiveStatusEffect>(),
    };

    private static PartyManager PartyOf(params ulong[] ids)
    {
        var list = ids.Select(Member).ToList();
        return new PartyManager(list);
    }

    [Fact]
    public void LeaderIsFirstMember()
    {
        var party = PartyOf(1, 2, 3);
        Assert.Equal(1ul, party.Leader.Id);
    }

    [Fact]
    public void EmptyPartyHasNoLeader()
    {
        Assert.Null(PartyOf().Leader);
    }

    [Fact]
    public void AddAppendsToBack()
    {
        var party = PartyOf(1);
        Assert.True(party.TryAddMember(Member(2)));
        Assert.Equal(new ulong[] { 1, 2 }, party.Members.Select(m => m.Id));
    }

    [Fact]
    public void AddRefusesDuplicateIds()
    {
        var party = PartyOf(1, 2);
        Assert.False(party.TryAddMember(Member(2)));
        Assert.Equal(2, party.Members.Count);
    }

    [Fact]
    public void AddRefusesBeyondMaxSize()
    {
        var party = PartyOf(1, 2, 3, 4);
        Assert.True(party.IsFull);
        Assert.False(party.TryAddMember(Member(5)));
        Assert.Equal(PartyManager.MaxSize, party.Members.Count);
    }

    [Fact]
    public void AddRefusesNull()
    {
        Assert.False(PartyOf(1).TryAddMember(null));
    }

    [Fact]
    public void RemoveDropsTheMember()
    {
        var party = PartyOf(1, 2, 3);
        Assert.True(party.RemoveMember(2));
        Assert.Equal(new ulong[] { 1, 3 }, party.Members.Select(m => m.Id));
    }

    [Fact]
    public void RemovingLeaderPromotesNextMember()
    {
        var party = PartyOf(1, 2, 3);
        Assert.True(party.RemoveMember(1));
        Assert.Equal(2ul, party.Leader.Id);
    }

    [Fact]
    public void LastMemberCannotBeRemoved()
    {
        var party = PartyOf(1);
        Assert.False(party.RemoveMember(1));
        Assert.Single(party.Members);
    }

    [Fact]
    public void RemoveUnknownIdFails()
    {
        Assert.False(PartyOf(1, 2).RemoveMember(9));
    }

    [Fact]
    public void SetLeaderMovesMemberToFrontKeepingOtherOrder()
    {
        var party = PartyOf(1, 2, 3, 4);
        Assert.True(party.SetLeader(3));
        Assert.Equal(new ulong[] { 3, 1, 2, 4 }, party.Members.Select(m => m.Id));
    }

    [Fact]
    public void SetLeaderOnCurrentLeaderIsANoOp()
    {
        var party = PartyOf(1, 2);
        Assert.True(party.SetLeader(1));
        Assert.Equal(new ulong[] { 1, 2 }, party.Members.Select(m => m.Id));
    }

    [Fact]
    public void SetLeaderUnknownIdFails()
    {
        Assert.False(PartyOf(1, 2).SetLeader(9));
    }

    [Fact]
    public void MoveSwapsNeighbors()
    {
        var party = PartyOf(1, 2, 3);
        Assert.True(party.Move(3, -1));
        Assert.Equal(new ulong[] { 1, 3, 2 }, party.Members.Select(m => m.Id));
        Assert.True(party.Move(1, +1));
        Assert.Equal(new ulong[] { 3, 1, 2 }, party.Members.Select(m => m.Id));
    }

    [Fact]
    public void MovePastEitherEndFails()
    {
        var party = PartyOf(1, 2);
        Assert.False(party.Move(1, -1));
        Assert.False(party.Move(2, +1));
        Assert.Equal(new ulong[] { 1, 2 }, party.Members.Select(m => m.Id));
    }

    [Fact]
    public void MoveIntoFrontChangesLeader()
    {
        var party = PartyOf(1, 2);
        Assert.True(party.Move(2, -1));
        Assert.Equal(2ul, party.Leader.Id);
    }
}
