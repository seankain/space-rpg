using Godot;
using System;

public partial class Level : Node3D
{
    [Export]
    public PackedScene PlayerScene;
    public override void _Ready()
    {
        AddPlayer();
    }

    private void AddPlayer()
    {
        var player = (Node3D)PlayerScene.Instantiate();
        AddChild(player);
        var state = SaveManager.Instance?.CurrentState;
        if (state?.PlayerPosition != null)
        {
            player.GlobalPosition = SaveManager.ToGodot(state.PlayerPosition.Value);
            if (state.PlayerRotation != null)
            {
                player.Rotation = SaveManager.ToGodot(state.PlayerRotation.Value);
            }
            AddFollowers(player, state, useSavedPositions: true);
            return;
        }
        foreach (var c in GetChildren())
        {
            if (c is Spawn spawn)
            {
                player.GlobalPosition = spawn.GlobalPosition;
                break;
            }
        }
        if (state != null)
        {
            AddFollowers(player, state, useSavedPositions: false);
        }
    }

    // Every party member beyond the leader walks the level as a follower
    // (party plan Phase 2). Saved member positions are only trusted when the
    // player's own position was restored (i.e. this is the level the party
    // was saved in) and the member is near enough to plausibly belong here;
    // anything else falls into line behind the leader.
    private void AddFollowers(Node3D player, GameState state, bool useSavedPositions)
    {
        var members = state.Party;
        if (members == null)
        {
            return;
        }
        for (var i = 1; i < members.Count; i++)
        {
            var member = members[i];
            var position = player.GlobalPosition + PartyMemberFollower.BehindLeaderOffset(i);
            if (useSavedPositions && member.Position != System.Numerics.Vector3.Zero)
            {
                var saved = SaveManager.ToGodot(member.Position);
                if (saved.DistanceTo(player.GlobalPosition) <= PartyMemberFollower.CatchUpDistance)
                {
                    position = saved;
                }
            }
            PartyMemberFollower.Spawn(this, member, i, position);
        }
    }

}
