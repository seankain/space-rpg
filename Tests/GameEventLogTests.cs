using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

// The event log's own rules: recording, ordering, filtering, the cap, and the
// static hook the toast layer and the Log tab listen on.
public class GameEventLogTests
{
    [Fact]
    public void RecordAppendsInOrderAndListsNewestFirst()
    {
        var log = new GameEventLog();
        log.Record(GameEventKind.Item, "Picked up Ration.");
        log.Record(GameEventKind.Credits, "Earned 20 credits.");

        Assert.Equal(
            new[] { "Picked up Ration.", "Earned 20 credits." },
            log.Entries.Select(e => e.Text));
        Assert.Equal(
            new[] { "Earned 20 credits.", "Picked up Ration." },
            log.Newest().Select(e => e.Text));
    }

    [Fact]
    public void NewestFiltersToOneKind()
    {
        var log = new GameEventLog();
        log.Record(GameEventKind.Item, "Picked up Ration.");
        log.Record(GameEventKind.Battle, "Battle started against Vex.");
        log.Record(GameEventKind.Item, "Picked up Medkit.");

        Assert.Equal(
            new[] { "Picked up Medkit.", "Picked up Ration." },
            log.Newest(GameEventKind.Item).Select(e => e.Text));
    }

    [Fact]
    public void BlankTextIsIgnored()
    {
        var log = new GameEventLog();
        Assert.Null(log.Record(GameEventKind.Item, "   "));
        Assert.Null(log.Record(GameEventKind.Item, null));
        Assert.Empty(log.Entries);
    }

    [Fact]
    public void CapDropsTheOldestEntries()
    {
        var log = new GameEventLog();
        for (var i = 0; i < GameEventLog.MaxEntries + 10; i++)
        {
            log.Record(GameEventKind.Item, $"Event {i}");
        }

        Assert.Equal(GameEventLog.MaxEntries, log.Entries.Count);
        Assert.Equal("Event 10", log.Entries[0].Text);
        Assert.Equal($"Event {GameEventLog.MaxEntries + 9}", log.Entries[^1].Text);
    }

    [Fact]
    public void RecordedFiresForEveryEntry()
    {
        // Recorded is static and xunit runs test classes in parallel, so the
        // listener only keeps entries carrying this run's marker.
        var marker = $"Earned 5 credits. [{Guid.NewGuid():N}]";
        var seen = new List<GameEvent>();
        void Listener(GameEvent entry)
        {
            if (entry.Text == marker)
            {
                seen.Add(entry);
            }
        }

        GameEventLog.Recorded += Listener;
        try
        {
            new GameEventLog().Record(GameEventKind.Credits, marker, notify: true);
        }
        finally
        {
            GameEventLog.Recorded -= Listener;
        }

        var recorded = Assert.Single(seen);
        Assert.Equal(GameEventKind.Credits, recorded.Kind);
        Assert.True(recorded.Notify);
    }

    [Fact]
    public void RecordEventSurvivesAStateWithNoLogBlock()
    {
        // A pre-v9 save deserializes with a null log until SaveRepository
        // backfills it; RecordEvent must not throw if anything gets there first.
        var state = new GameState { EventLog = null };

        state.RecordEvent(GameEventKind.Conversation, "Spoke with Hale.");

        Assert.Equal("Spoke with Hale.", Assert.Single(state.EventLog.Entries).Text);
    }
}

// The gameplay side: the things the log is supposed to notice actually record.
public class GameEventRecordingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"spacerpg-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static DialogueContext Context(GameState state) =>
        new() { State = state, SpeakerName = "Hale" };

    [Fact]
    public void GiveItemRecordsAnItemEventThatToasts()
    {
        var state = new GameState();

        DialogueEffects.Run(EffectRef.Parse($"give_item:{ItemCatalog.MedkitId}:2"), Context(state));

        var entry = Assert.Single(state.EventLog.Newest(GameEventKind.Item));
        Assert.Equal("Received Medkit x2 from Hale.", entry.Text);
        Assert.True(entry.Notify);
    }

    [Fact]
    public void TakeItemRecordsQuietly()
    {
        var state = new GameState();
        state.Inventory.Add(ItemCatalog.MaguffinCubeId);

        DialogueEffects.Run(EffectRef.Parse($"take_item:{ItemCatalog.MaguffinCubeId}"), Context(state));

        var entry = Assert.Single(state.EventLog.Newest(GameEventKind.Item));
        Assert.Equal("Handed over Maguffin Cube to Hale.", entry.Text);
        Assert.False(entry.Notify);
    }

    [Fact]
    public void CreditsEffectRecordsTheActualChangeNotTheRequestedOne()
    {
        var state = new GameState { Credits = 100 };

        DialogueEffects.Run(EffectRef.Parse("credits:50"), Context(state));
        DialogueEffects.Run(EffectRef.Parse("credits:-500"), Context(state));

        var entries = state.EventLog.Newest(GameEventKind.Credits).ToList();
        Assert.Equal("Paid 150 credits to Hale.", entries[0].Text);
        Assert.False(entries[0].Notify);
        Assert.Equal("Earned 50 credits from Hale.", entries[1].Text);
        Assert.True(entries[1].Notify);
    }

    [Fact]
    public void QuestMovesAreLoggedOnceEachTheyActuallyMove()
    {
        var state = new GameState();
        var advance = EffectRef.Parse($"advance_quest:{QuestCatalog.ReturnTheMaguffinId}");

        DialogueEffects.Run(advance, Context(state));
        DialogueEffects.Run(advance, Context(state));
        // Clamped at Success; replaying the conversation must not re-log it.
        DialogueEffects.Run(advance, Context(state));

        var title = QuestCatalog.Get(QuestCatalog.ReturnTheMaguffinId).Title;
        Assert.Equal(
            new[] { $"Completed the quest '{title}'.", $"Started the quest '{title}'." },
            state.EventLog.Newest(GameEventKind.Quest).Select(e => e.Text));
    }

    [Fact]
    public void BuyingLogsTheItemAndSellingLogsTheCredits()
    {
        var state = new GameState { Credits = 500 };
        var merchant = new Merchant { Name = "Sable", Credits = 500 };
        merchant.Stock.Add(ItemCatalog.MedkitId);

        Assert.True(Trade.TryBuy(state, merchant, ItemCatalog.MedkitId, out _));
        Assert.True(Trade.TrySell(state, merchant, ItemCatalog.MedkitId, out _));

        var medkit = ItemCatalog.Get(ItemCatalog.MedkitId);
        var bought = Assert.Single(state.EventLog.Newest(GameEventKind.Item));
        Assert.Equal($"Bought Medkit from Sable for {Trade.BuyPrice(medkit)} credits.", bought.Text);
        var sold = Assert.Single(state.EventLog.Newest(GameEventKind.Credits));
        Assert.Equal($"Sold Medkit to Sable for {Trade.SellPrice(medkit)} credits.", sold.Text);
        Assert.True(bought.Notify);
        Assert.True(sold.Notify);
    }

    [Fact]
    public void FailedTradesLogNothing()
    {
        var state = new GameState { Credits = 0 };
        var merchant = new Merchant { Name = "Sable", Credits = 0 };
        merchant.Stock.Add(ItemCatalog.MedkitId);

        Assert.False(Trade.TryBuy(state, merchant, ItemCatalog.MedkitId, out _));
        Assert.False(Trade.TrySell(state, merchant, ItemCatalog.MedkitId, out _));

        Assert.Empty(state.EventLog.Entries);
    }

    [Fact]
    public void EventLogRoundTripsThroughASave()
    {
        var repository = new SaveRepository(root);
        var meta = new SaveData { SlotId = Guid.NewGuid(), SaveNumber = 1 };
        var state = new GameState
        {
            CurrentLevelPath = "res://Scenes/Levels/Intro.tscn",
            LocationName = "Intro Station",
        };
        state.RecordEvent(GameEventKind.Battle, "Defeated Vex and gained 12 XP.", notify: true);
        state.RecordEvent(GameEventKind.Conversation, "Spoke with Hale.");

        repository.Save(meta, state);
        var loaded = repository.LoadState(meta.SlotId);

        Assert.Equal(
            new[] { "Defeated Vex and gained 12 XP.", "Spoke with Hale." },
            loaded.EventLog.Entries.Select(e => e.Text));
        Assert.Equal(GameEventKind.Battle, loaded.EventLog.Entries[0].Kind);
        // Notify is a play-time flag, not saved state: a restored log must not
        // re-toast its history.
        Assert.False(loaded.EventLog.Entries[0].Notify);
    }

    [Fact]
    public void PreV9SaveLoadsWithAnEmptyLog()
    {
        var repository = new SaveRepository(root);
        var slotDirectory = Path.Combine(root, $"slot_{Guid.NewGuid():N}");
        Directory.CreateDirectory(slotDirectory);
        var slotId = Guid.Parse(Path.GetFileName(slotDirectory)["slot_".Length..]);
        File.WriteAllText(
            Path.Combine(slotDirectory, "state.json"),
            """{"CurrentLevelPath":"res://Scenes/Levels/Intro.tscn"}""");

        var loaded = repository.LoadState(slotId);

        Assert.NotNull(loaded.EventLog);
        Assert.Empty(loaded.EventLog.Entries);
    }
}
