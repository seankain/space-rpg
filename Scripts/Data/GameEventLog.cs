using System;
using System.Collections.Generic;
using System.Linq;

// The running game's history of notable moments — items acquired, credits
// earned and spent, battles, conversations, quest moves, party changes. Lives
// on GameState, so it saves and loads with everything else, and is capped so a
// long run can't grow the save file without bound.
//
// Engine-free: gameplay code records into it directly (Trade, DialogueEffects
// and friends never touch Godot), and the Godot-side presentation — the Log tab
// and the toast layer — listens through the static Recorded event below.
public class GameEventLog
{
    // Oldest entries are dropped past this. Deep enough that the Log tab reads
    // as a history rather than a ticker, shallow enough to stay a small chunk
    // of the save file.
    public const int MaxEntries = 250;

    // Oldest first, the order they happened in; the UI reverses for display.
    public List<GameEvent> Entries {get;set;} = new();

    // Raised for every entry recorded into any log. Static because the
    // listeners (the toast layer, the Log tab) outlive any one GameState:
    // loading a save swaps the log instance underneath them, and a static hook
    // means nothing has to be re-subscribed when it does. Subscribers must
    // unsubscribe when they leave the tree or they leak.
    public static event Action<GameEvent> Recorded;

    // Records a line and hands it to any listener. Blank text is ignored so a
    // caller that couldn't name what happened logs nothing rather than an empty
    // row. Returns the entry (null when ignored) for callers that want it.
    public GameEvent Record(GameEventKind kind, string text, bool notify = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        var entry = new GameEvent
        {
            Kind = kind,
            Text = text.Trim(),
            Time = DateTime.Now,
            Notify = notify,
        };
        Entries ??= new List<GameEvent>();
        Entries.Add(entry);
        if (Entries.Count > MaxEntries)
        {
            Entries.RemoveRange(0, Entries.Count - MaxEntries);
        }
        Recorded?.Invoke(entry);
        return entry;
    }

    // Newest first, optionally narrowed to one kind — the order the Log tab
    // lists them in.
    public IEnumerable<GameEvent> Newest(GameEventKind? kind = null) =>
        (Entries ?? new List<GameEvent>())
            .Where(entry => kind == null || entry.Kind == kind.Value)
            .Reverse();
}
