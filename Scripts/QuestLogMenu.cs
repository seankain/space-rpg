using Godot;
using System.Collections.Generic;
using System.Linq;

// The Quests tab of the in-game menu: every quest the player has picked up,
// grouped into main/side/completed/failed sections; selecting a quest shows
// its details, its current objectives, and — for a quest still in progress —
// starts tracking it, which is what puts its markers on the Map tab
// (quest-markers.md Phase 3).
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

	// Built in code and appended to the scene's details column (the same choice
	// MapMenu makes for its whole tab): the objective line and the tracking
	// controls are this phase's addition, and adding them here keeps the
	// existing scene file untouched.
	private Label objectivesLabel;
	private Button trackButton;
	private Button showOnMapButton;

	// The quest whose details are currently shown, so the buttons act on it.
	private uint? shownQuestId;

	public override void _Ready()
	{
		QuestList.ItemSelected += OnQuestSelected;
		BuildTrackingControls();
		VisibilityChanged += () =>
		{
			if (IsVisibleInTree())
			{
				Refresh();
			}
		};
	}

	private void BuildTrackingControls()
	{
		// The details column the scene declares (title / status / description).
		if (QuestDescriptionLabel?.GetParent() is not Control details)
		{
			return;
		}

		objectivesLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		details.AddChild(objectivesLabel);

		var actions = new HBoxContainer();
		trackButton = new Button { Text = TrackText };
		trackButton.Pressed += OnTrackPressed;
		showOnMapButton = new Button { Text = "Show on Map" };
		showOnMapButton.Pressed += ShowOnMap;
		actions.AddChild(trackButton);
		actions.AddChild(showOnMapButton);
		details.AddChild(actions);
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
		// Reopening the menu comes back to the quest the player is following
		// rather than to an empty panel.
		if (!SelectQuest(state.TrackedQuestId))
		{
			ShowDetails(null, QUESTSUCCESSSTATE.Unstarted);
		}
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
			QuestList.AddItem(RowText(entry.Quest));
		}
	}

	// The tracked quest wears a marker in the list, so "which one am I
	// following" survives scrolling away from the details panel.
	private string RowText(Quest quest) =>
		SaveManager.Instance?.CurrentState?.TrackedQuestId == quest.Id
			? "  ▸ " + quest.Title
			: "    " + quest.Title;

	// Re-labels the existing rows after tracking moves. Deliberately not a
	// Refresh: rebuilding the list re-selects the tracked quest, which tracks
	// it again — a loop. Only the ▸ needs to move.
	private void UpdateRowLabels()
	{
		for (var i = 0; i < listedQuestIds.Count; i++)
		{
			if (listedQuestIds[i] is { } questId && QuestCatalog.Get(questId) is { } quest)
			{
				QuestList.SetItemText(i, RowText(quest));
			}
		}
	}

	// Selects the row for a quest, if it is listed. Returns false when it isn't
	// (nothing tracked, or a tracked quest the log doesn't show).
	private bool SelectQuest(uint questId)
	{
		if (questId == 0)
		{
			return false;
		}
		var index = listedQuestIds.IndexOf(questId);
		if (index < 0)
		{
			return false;
		}
		QuestList.Select(index);
		OnQuestSelected(index);
		return true;
	}

	private void OnQuestSelected(long index)
	{
		var questId = listedQuestIds[(int)index];
		if (questId == null)
		{
			return;
		}
		var state = SaveManager.Instance.CurrentState;
		// Selecting an active quest is what tracks it — the feature as asked
		// for: pick a quest, see it on the map. Selecting a finished one only
		// reads it, and leaves whatever is tracked alone.
		if (state.SetTrackedQuest(questId.Value))
		{
			UpdateRowLabels();
		}
		ShowDetails(QuestCatalog.Get(questId.Value), state.GetQuestState(questId.Value));
	}

	private void ShowDetails(Quest quest, QUESTSUCCESSSTATE state)
	{
		shownQuestId = quest?.Id;
		if (quest == null)
		{
			var logIsEmpty = listedQuestIds.Count == 0;
			QuestTitleLabel.Text = logIsEmpty ? "No Quests" : "";
			QuestStatusLabel.Text = "";
			QuestDescriptionLabel.Text = logIsEmpty ? "You haven't picked up any quests yet. Talk to the people around the station." : "";
			UpdateTrackingControls(null, state);
			return;
		}
		QuestTitleLabel.Text = quest.Title;
		QuestStatusLabel.Text = $"{(quest.SideQuest ? "Side Quest" : "Main Quest")} — {StatusText(state)}";
		QuestDescriptionLabel.Text = quest.Description;
		UpdateTrackingControls(quest, state);
	}

	// The objective lines and the two buttons, all driven by the marker
	// resolver: an in-progress quest lists where it wants the player to go
	// (positions aren't needed here — the Map tab resolves those), and a
	// finished one offers no tracking.
	private void UpdateTrackingControls(Quest quest, QUESTSUCCESSSTATE questState)
	{
		if (objectivesLabel == null)
		{
			return;
		}
		var state = SaveManager.Instance?.CurrentState;
		var trackable = quest != null && questState == QUESTSUCCESSSTATE.InProgress;
		var objectives = quest == null
			? new List<string>()
			: QuestMarkerResolver.ActiveMarkers(state, quest).Select(marker => marker.Label).ToList();

		objectivesLabel.Visible = objectives.Count > 0;
		objectivesLabel.Text = objectives.Count > 0
			? "Objectives:\n" + string.Join("\n", objectives.Select(label => "  • " + label))
			: "";

		trackButton.Visible = trackable;
		trackButton.Disabled = state?.TrackedQuestId == quest?.Id;
		trackButton.Text = trackButton.Disabled ? TrackedText : TrackText;
		showOnMapButton.Visible = trackable;
	}

	private void OnTrackPressed()
	{
		var state = SaveManager.Instance?.CurrentState;
		if (state == null || shownQuestId == null)
		{
			return;
		}
		var questId = shownQuestId.Value;
		state.SetTrackedQuest(questId);
		UpdateRowLabels();
		ShowDetails(QuestCatalog.Get(questId), state.GetQuestState(questId));
	}

	// Hands the player to the Map tab, framed on the tracked quest's nearest
	// objective rather than on themselves. The tab is found by its script
	// rather than by index, so reordering the tabs in the scene can't send the
	// player somewhere else; switching to it runs MapMenu's own refresh
	// (VisibilityChanged) before the focus call.
	private void ShowOnMap()
	{
		if (GetParent() is not TabContainer tabs)
		{
			return;
		}
		for (var i = 0; i < tabs.GetTabCount(); i++)
		{
			if (tabs.GetTabControl(i) is MapMenu map)
			{
				tabs.CurrentTab = i;
				map.FocusTrackedObjective();
				return;
			}
		}
	}

	private const string TrackText = "Track";
	private const string TrackedText = "Tracked";

	private static string StatusText(QUESTSUCCESSSTATE state) => state switch
	{
		QUESTSUCCESSSTATE.InProgress => "In Progress",
		QUESTSUCCESSSTATE.Success => "Completed",
		QUESTSUCCESSSTATE.Failed => "Failed",
		_ => "Not Started",
	};
}
