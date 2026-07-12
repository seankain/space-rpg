using Godot;
using System.Collections.Generic;
using System.Linq;

// The Quests tab of the in-game menu: every quest the player has picked up,
// grouped into main/side/completed/failed sections; selecting a quest shows
// its details.
public partial class QuestLogMenu : Control
{
	[Export]
	public ItemList QuestList;

	[Export]
	public Label QuestTitleLabel;

	[Export]
	public Label QuestStatusLabel;

	[Export]
	public Label QuestDescriptionLabel;

	// Quest ids backing the listed rows, in display order; null rows are
	// non-selectable section headers.
	private readonly List<uint?> listedQuestIds = new();

	public override void _Ready()
	{
		QuestList.ItemSelected += OnQuestSelected;
		VisibilityChanged += () =>
		{
			if (IsVisibleInTree())
			{
				Refresh();
			}
		};
	}

	public void Refresh()
	{
		listedQuestIds.Clear();
		QuestList.Clear();
		var state = SaveManager.Instance?.CurrentState;
		if (state == null)
		{
			ShowDetails(null, QUESTSUCCESSSTATE.Unstarted);
			return;
		}
		// Quests the player has actually picked up (an Unstarted entry can
		// only come from a hand-edited save; hide it like a missing entry).
		var known = state.Quests
			.Where(progress => progress.State != QUESTSUCCESSSTATE.Unstarted)
			.Select(progress => (Progress: progress, Quest: QuestCatalog.Get(progress.QuestId)))
			.Where(entry => entry.Quest != null)
			.ToList();
		AddSection("Main Quests", known.Where(e => e.Progress.State == QUESTSUCCESSSTATE.InProgress && !e.Quest.SideQuest));
		AddSection("Side Quests", known.Where(e => e.Progress.State == QUESTSUCCESSSTATE.InProgress && e.Quest.SideQuest));
		AddSection("Completed", known.Where(e => e.Progress.State == QUESTSUCCESSSTATE.Success));
		AddSection("Failed", known.Where(e => e.Progress.State == QUESTSUCCESSSTATE.Failed));
		ShowDetails(null, QUESTSUCCESSSTATE.Unstarted);
	}

	private void AddSection(string title, IEnumerable<(QuestProgress Progress, Quest Quest)> entries)
	{
		var section = entries.ToList();
		if (section.Count == 0)
		{
			return;
		}
		listedQuestIds.Add(null);
		var headerIndex = QuestList.AddItem(title, null, false);
		QuestList.SetItemDisabled(headerIndex, true);
		foreach (var entry in section)
		{
			listedQuestIds.Add(entry.Quest.Id);
			QuestList.AddItem("    " + entry.Quest.Title);
		}
	}

	private void OnQuestSelected(long index)
	{
		var questId = listedQuestIds[(int)index];
		if (questId == null)
		{
			return;
		}
		var state = SaveManager.Instance.CurrentState;
		ShowDetails(QuestCatalog.Get(questId.Value), state.GetQuestState(questId.Value));
	}

	private void ShowDetails(Quest quest, QUESTSUCCESSSTATE state)
	{
		if (quest == null)
		{
			var logIsEmpty = listedQuestIds.Count == 0;
			QuestTitleLabel.Text = logIsEmpty ? "No Quests" : "";
			QuestStatusLabel.Text = "";
			QuestDescriptionLabel.Text = logIsEmpty ? "You haven't picked up any quests yet. Talk to the people around the station." : "";
			return;
		}
		QuestTitleLabel.Text = quest.Title;
		QuestStatusLabel.Text = $"{(quest.SideQuest ? "Side Quest" : "Main Quest")} — {StatusText(state)}";
		QuestDescriptionLabel.Text = quest.Description;
	}

	private static string StatusText(QUESTSUCCESSSTATE state) => state switch
	{
		QUESTSUCCESSSTATE.InProgress => "In Progress",
		QUESTSUCCESSSTATE.Success => "Completed",
		QUESTSUCCESSSTATE.Failed => "Failed",
		_ => "Not Started",
	};
}
