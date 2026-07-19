using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// The Party tab of the in-game menu (party plan Phase 3): the roster on the
// left with the leader starred and vitals inline; the selected member's sheet
// on the right (level/XP, HP/PP, stats, equipment); and the management
// controls — Set Leader, Move Up / Move Down (order is the leader and the
// future combat turn layout), and Dismiss behind a confirmation dialog.
public partial class PartyMenu : Control
{
	[Export]
	public ItemList MemberList;

	[Export]
	public Label MemberNameLabel;

	[Export]
	public Label MemberRoleLabel;

	[Export]
	public Label MemberVitalsLabel;

	[Export]
	public Label MemberStatsLabel;

	[Export]
	public Label EquippedLabel;

	[Export]
	public Button SetLeaderButton;

	[Export]
	public Button MoveUpButton;

	[Export]
	public Button MoveDownButton;

	[Export]
	public Button DismissButton;

	[Export]
	public Label ActionResultLabel;

	[Export]
	public ConfirmationDialog DismissConfirm;

	private static readonly (EQUIPSLOT Slot, string Name)[] SlotNames =
	{
		(EQUIPSLOT.HEAD, "Head"),
		(EQUIPSLOT.EYES, "Eyes"),
		(EQUIPSLOT.LEFT_HAND, "Left Hand"),
		(EQUIPSLOT.RIGHT_HAND, "Right Hand"),
		(EQUIPSLOT.CHEST, "Chest"),
		(EQUIPSLOT.LEGS, "Legs"),
	};

	// Member id of the selected row; survives Refresh so reordering a member
	// keeps them selected as they move through the list.
	private ulong? selectedMemberId;

	public override void _Ready()
	{
		MemberList.ItemSelected += OnMemberSelected;
		SetLeaderButton.Pressed += () => Reorder(party => party.SetLeader(selectedMemberId.Value),
			name => $"{name} now leads the party.");
		MoveUpButton.Pressed += () => Reorder(party => party.Move(selectedMemberId.Value, -1), null);
		MoveDownButton.Pressed += () => Reorder(party => party.Move(selectedMemberId.Value, +1), null);
		DismissButton.Pressed += OnDismissPressed;
		DismissConfirm.Confirmed += OnDismissConfirmed;
		VisibilityChanged += () =>
		{
			if (IsVisibleInTree())
			{
				ActionResultLabel.Text = "";
				Refresh();
			}
		};
	}

	public void Refresh()
	{
		MemberList.Clear();
		var members = Party()?.Members;
		if (members == null || members.Count == 0)
		{
			selectedMemberId = null;
			UpdateDetails();
			return;
		}
		if (selectedMemberId == null || members.All(m => m.Id != selectedMemberId))
		{
			selectedMemberId = members[0].Id;
		}
		for (var i = 0; i < members.Count; i++)
		{
			var m = members[i];
			var star = i == 0 ? "★ " : "    ";
			MemberList.AddItem($"{star}{m.Name} — Lv {m.Level} — HP {m.HealthPoints}/{m.MaxHealthPoints} — PP {m.MagicPoints}/{m.MaxMagicPoints}");
			if (m.Id == selectedMemberId)
			{
				MemberList.Select(i);
			}
		}
		UpdateDetails();
	}

	private void OnMemberSelected(long index)
	{
		var members = Party()?.Members;
		if (members != null && index >= 0 && index < members.Count)
		{
			selectedMemberId = members[(int)index].Id;
		}
		ActionResultLabel.Text = "";
		UpdateDetails();
	}

	private void UpdateDetails()
	{
		var party = Party();
		var member = SelectedMember();
		MemberNameLabel.Text = member?.Name ?? "";
		MemberRoleLabel.Text = member == null ? "" : (member == party.Leader ? "Party leader" : "Party member");
		MemberVitalsLabel.Text = VitalsText(member);
		MemberStatsLabel.Text = StatsText(member);
		EquippedLabel.Text = EquippedText(member);

		var index = member == null ? -1 : IndexOf(party, member.Id);
		SetLeaderButton.Disabled = member == null || index == 0;
		MoveUpButton.Disabled = member == null || index <= 0;
		MoveDownButton.Disabled = member == null || index < 0 || index >= party.Members.Count - 1;
		// The leader is the controlled character; hand leadership off first.
		DismissButton.Disabled = member == null || index == 0 || party.Members.Count <= 1;
	}

	// Shared plumbing for the three ordering buttons: run the roster change,
	// then re-sync the list, the sheet, and the followers walking the level.
	private void Reorder(Func<PartyManager, bool> change, Func<string, string> message)
	{
		var party = Party();
		var member = SelectedMember();
		if (party == null || member == null || !change(party))
		{
			return;
		}
		RebuildFollowers();
		Refresh();
		ActionResultLabel.Text = message?.Invoke(member.Name) ?? "";
	}

	private void OnDismissPressed()
	{
		var member = SelectedMember();
		if (member == null)
		{
			return;
		}
		DismissConfirm.DialogText = $"Dismiss {member.Name} from the party? Their equipped gear returns to the shared inventory.";
		DismissConfirm.PopupCentered();
	}

	private void OnDismissConfirmed()
	{
		var state = SaveManager.Instance?.CurrentState;
		var party = Party();
		var member = SelectedMember();
		if (state == null || party == null || member == null || !party.RemoveMember(member.Id))
		{
			return;
		}
		// Gear would be lost with the member (a re-recruit builds a fresh
		// CharacterEntity), so strip it back into the shared inventory.
		foreach (var (slot, _) in SlotNames)
		{
			var itemId = member.EquipSlots?.GetItemId(slot);
			if (itemId != null)
			{
				state.Inventory.Add(itemId.Value);
				member.EquipSlots.SetItemId(slot, null);
			}
		}
		RebuildFollowers();
		selectedMemberId = null;
		Refresh();
		// A dismissed RecruitNpc stands at his spawn spot again next time the
		// area loads, ready to be re-recruited.
		ActionResultLabel.Text = $"{member.Name} left the party.";
	}

	// Roster changes reshuffle who is a follower and in what order, so drop
	// every follower body and respawn the line behind the player. Followers
	// respawn wearing their own NPC mesh; the leader always wears the
	// player's Knight body.
	private void RebuildFollowers()
	{
		var tree = GetTree();
		foreach (var node in tree.GetNodesInGroup(PartyMemberFollower.GroupName))
		{
			if (node is PartyMemberFollower follower)
			{
				follower.QueueFree();
				// QueueFree leaves the node grouped until end of frame; make
				// sure a same-frame rebuild doesn't count the corpse.
				follower.RemoveFromGroup(PartyMemberFollower.GroupName);
			}
		}
		var members = Party()?.Members;
		if (members == null || tree.GetFirstNodeInGroup(Player.GroupName) is not Node3D player)
		{
			return;
		}
		for (var i = 1; i < members.Count; i++)
		{
			PartyMemberFollower.Spawn(player.GetParent(), members[i],
				i, player.GlobalPosition + PartyMemberFollower.BehindLeaderOffset(i));
		}
	}

	private PartyManager Party()
	{
		var partyList = SaveManager.Instance?.CurrentState?.Party;
		return partyList == null ? null : new PartyManager(partyList);
	}

	private CharacterEntity SelectedMember() =>
		selectedMemberId == null ? null : Party()?.Get(selectedMemberId.Value);

	private static int IndexOf(PartyManager party, ulong memberId)
	{
		for (var i = 0; i < party.Members.Count; i++)
		{
			if (party.Members[i].Id == memberId)
			{
				return i;
			}
		}
		return -1;
	}

	private static string VitalsText(CharacterEntity member) => member == null ? "" :
		$"Level {member.Level} — {member.ExperiencePoints} XP\nHP {member.HealthPoints}/{member.MaxHealthPoints}    PP {member.MagicPoints}/{member.MaxMagicPoints}";

	private static string StatsText(CharacterEntity member)
	{
		if (member?.Stats == null)
		{
			return "";
		}
		var s = member.Stats;
		return $"STR {s.Strength}    INT {s.Intelligence}    CON {s.Constitution}\nDEX {s.Dexterity}    WIS {s.Wisdom}    CHA {s.Charisma}";
	}

	private static string EquippedText(CharacterEntity member)
	{
		if (member == null)
		{
			return "";
		}
		var lines = new List<string>();
		foreach (var (slot, name) in SlotNames)
		{
			var itemId = member.EquipSlots?.GetItemId(slot);
			if (itemId != null)
			{
				lines.Add($"  {name}: {ItemCatalog.Get(itemId.Value)?.Name ?? "(unknown item)"}");
			}
		}
		return lines.Count == 0
			? "Nothing equipped. Gear is equipped from the Inventory tab."
			: $"Equipped:\n{string.Join("\n", lines)}";
	}
}
