using System;
using System.Collections.Generic;
using System.Linq;

// Engine-free roster rules for GameState.Party (party plan Phase 1): an
// ordered member list whose index 0 is the leader — the character the player
// controls in the world and who fronts the battle line. A thin wrapper
// constructed around the live list; it holds no state of its own, so any
// system can wrap the current party wherever roster rules are needed.
public class PartyManager
{
    // Active party cap (party plan: fits turn-based encounter design and
    // follower pathing cost). A bench beyond this waits on content needing it.
    public const int MaxSize = 4;

    private readonly List<CharacterEntity> members;

    public PartyManager(List<CharacterEntity> members)
    {
        this.members = members ?? throw new ArgumentNullException(nameof(members));
    }

    public IReadOnlyList<CharacterEntity> Members => members;

    public CharacterEntity Leader => members.Count > 0 ? members[0] : null;

    public bool IsFull => members.Count >= MaxSize;

    public bool Contains(ulong memberId) => members.Any(m => m.Id == memberId);

    public CharacterEntity Get(ulong memberId) => members.FirstOrDefault(m => m.Id == memberId);

    // Adds to the back of the roster. Duplicates and additions past MaxSize
    // are refused rather than thrown: callers (the recruit dialogue) turn a
    // false into user-facing feedback.
    public bool TryAddMember(CharacterEntity member)
    {
        if (member == null || IsFull || Contains(member.Id))
        {
            return false;
        }
        members.Add(member);
        return true;
    }

    // The last member can never leave (someone has to be the player);
    // removing the leader promotes whoever is next in order.
    public bool RemoveMember(ulong memberId)
    {
        if (members.Count <= 1)
        {
            return false;
        }
        var member = Get(memberId);
        if (member == null)
        {
            return false;
        }
        members.Remove(member);
        return true;
    }

    // Moves the member to the front, preserving the relative order of the
    // rest. Reordering is how the leader changes (party plan Phase 3).
    public bool SetLeader(ulong memberId)
    {
        var member = Get(memberId);
        if (member == null)
        {
            return false;
        }
        if (members[0] == member)
        {
            return true;
        }
        members.Remove(member);
        members.Insert(0, member);
        return true;
    }

    // Swaps the member one place up (delta -1) or down (delta +1) the order.
    // Moving into index 0 makes them the leader.
    public bool Move(ulong memberId, int delta)
    {
        var member = Get(memberId);
        if (member == null)
        {
            return false;
        }
        var from = members.IndexOf(member);
        var to = from + delta;
        if (to < 0 || to >= members.Count)
        {
            return false;
        }
        (members[from], members[to]) = (members[to], members[from]);
        return true;
    }
}
