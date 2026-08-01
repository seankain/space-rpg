using System;
using System.Text.Json.Serialization;

// What kind of thing happened, so the Log tab can filter and the toast layer
// can style an entry without parsing its text. Ids are persisted by name in
// saves, so entries can be added but existing names must not be renamed.
public enum GameEventKind
{
    Item,
    Credits,
    Battle,
    Conversation,
    Quest,
    Party,
}

// One line in the running game's event log: something the player did or was
// given, already phrased for display. Engine-free like the rest of
// Scripts/Data so it serializes straight into GameState.
public class GameEvent
{
    public GameEventKind Kind {get;set;}

    // Player-facing sentence ("Picked up Ration x2."). Composed at record
    // time rather than from ids at display time, so a log entry keeps saying
    // what happened even if the catalogs are reworded later.
    public string Text {get;set;}

    // Wall-clock time the entry was recorded, used to group the log by
    // session date; there is no in-game clock to key off yet.
    public DateTime Time {get;set;}

    // Whether this entry also deserves a transient on-screen toast. A play-time
    // presentation decision, not saved state: a log restored from disk shows
    // its history without re-toasting it.
    [JsonIgnore]
    public bool Notify {get;set;}
}
