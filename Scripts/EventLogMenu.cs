using Godot;
using System.Collections.Generic;
using System.Linq;

// The Log tab of the in-game menu: everything the party has done this run —
// items picked up or traded, credits earned, battles, conversations, quest
// moves — newest first, narrowable to one kind with the dropdown. The record
// itself lives in GameState.EventLog and saves with the game; this is the view
// over it. The transient corner notifications are EventToasts.
public partial class EventLogMenu : Control
{
	[Export]
	public ItemList EventList;

	[Export]
	public OptionButton FilterSelect;

	[Export]
	public Label SummaryLabel;

	// Index-aligned with the dropdown's entries; index 0 (null) is "everything".
	private readonly List<GameEventKind?> filters = new();

	public override void _Ready()
	{
		BuildFilterOptions();
		FilterSelect.ItemSelected += _ => Refresh();
		VisibilityChanged += () =>
		{
			if (IsVisibleInTree())
			{
				Refresh();
			}
		};
		// The tab can be open while the world keeps running (the in-game menu
		// doesn't pause the tree), so follow the log live rather than only
		// rebuilding when the tab is opened.
		GameEventLog.Recorded += OnEventRecorded;
	}

	public override void _ExitTree()
	{
		GameEventLog.Recorded -= OnEventRecorded;
	}

	private void OnEventRecorded(GameEvent entry)
	{
		if (IsInstanceValid(this) && IsVisibleInTree())
		{
			Refresh();
		}
	}

	public void Refresh()
	{
		EventList.Clear();
		var log = SaveManager.Instance?.CurrentState?.EventLog;
		var kind = SelectedFilter;
		var entries = log?.Newest(kind).ToList() ?? new List<GameEvent>();

		foreach (var entry in entries)
		{
			var index = EventList.AddItem(FormatRow(entry, showKind: kind == null));
			// Rows are a record, not a menu: nothing happens when you click one.
			EventList.SetItemSelectable(index, false);
			EventList.SetItemCustomFgColor(index, GameEventDisplay.ColorOf(entry.Kind));
		}

		if (SummaryLabel != null)
		{
			SummaryLabel.Text = Summary(log, kind, entries.Count);
		}
	}

	private GameEventKind? SelectedFilter
	{
		get
		{
			var index = FilterSelect.Selected;
			return index >= 0 && index < filters.Count ? filters[index] : null;
		}
	}

	// "14:32  [Items]  Picked up Ration x2." — the kind prefix is dropped when
	// the list is already filtered to one kind and every row would repeat it.
	private static string FormatRow(GameEvent entry, bool showKind)
	{
		var time = entry.Time == default ? "--:--" : entry.Time.ToString("HH:mm");
		return showKind
			? $"{time}  [{GameEventDisplay.LabelOf(entry.Kind)}]  {entry.Text}"
			: $"{time}  {entry.Text}";
	}

	private static string Summary(GameEventLog log, GameEventKind? kind, int shown)
	{
		if (log == null)
		{
			return "No game in progress.";
		}
		if (shown > 0)
		{
			return kind == null
				? $"{shown} events"
				: $"{shown} {GameEventDisplay.LabelOf(kind.Value).ToLower()} events";
		}
		return log.Entries.Count == 0
			? "Nothing has happened yet. Go pick something up."
			: "No events of this kind yet.";
	}

	private void BuildFilterOptions()
	{
		FilterSelect.Clear();
		filters.Clear();
		FilterSelect.AddItem("Everything");
		filters.Add(null);
		foreach (var kind in GameEventDisplay.Kinds)
		{
			FilterSelect.AddItem(GameEventDisplay.LabelOf(kind));
			filters.Add(kind);
		}
		FilterSelect.Selected = 0;
	}
}
