using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

public class GameState
{
    public string CurrentLevelPath {get;set;}
    public string LocationName {get;set;}
    public Guid LocationId {get;set;}
    // Null until first captured; a fresh game spawns at the level's Spawn marker instead.
    public Vector3? PlayerPosition {get;set;}
    public Vector3? PlayerRotation {get;set;}
    // Where the exit door of the current interior (shop, house) returns the
    // player: captured by the entrance Door, consumed by the exit Door. All
    // null while the player is outside.
    public string ReturnLevelPath {get;set;}
    public string ReturnLocationName {get;set;}
    public Vector3? ReturnPosition {get;set;}
    public Vector3? ReturnRotation {get;set;}
    // Party-shared credits for trading with merchants. The initializer doubles
    // as the migration default: pre-v5 saves have no Credits field, so they
    // load with the new-game amount.
    public uint Credits {get;set;} = StartingCredits;
    public const uint StartingCredits = 250;
    public List<CharacterEntity> Party{get;set;} = new();
    // Shared party inventory; equipment stays per-character on EquipSlots.
    public Inventory Inventory {get;set;} = new();
    // Per-quest progress against QuestCatalog definitions; quests the player
    // hasn't touched have no entry (implicitly Unstarted).
    public List<QuestProgress> Quests {get;set;} = new();
    // Stable NpcIds (NpcDefinition.NpcId, e.g. "intro.vex") of battle NPCs
    // the party has beaten, so defeated challengers stay down across saves
    // and chunk reloads and quests can check for them. Pre-v7 saves stored
    // display names; SaveManager migrates those through NpcDatabase on load.
    public List<string> DefeatedNpcs {get;set;} = new();
    // Ad-hoc world flags a conversation sets and reads — "met_hale",
    // "hale_mood" — the general-purpose slot the quest/inventory/defeated-NPC
    // state doesn't cover (npc-dialogue-yarn.md Phase 3). Values are strings so
    // a flag can carry a little state ("angry", "2") as well as a yes/no.
    // Pre-v8 saves have no Flags and load with none.
    //
    // Keys are normalized (trimmed, lower-cased) by the accessors below rather
    // than by a case-insensitive comparer, because System.Text.Json rebuilds
    // this dictionary with the default comparer on load — a comparer set here
    // would silently apply to new games only.
    public Dictionary<string, string> Flags {get;set;} = new();
    // What the party has done: items picked up, credits earned, battles,
    // conversations. Capped and display-ready (see GameEventLog); the in-game
    // menu's Log tab reads it and the toast layer listens for new entries.
    // Pre-v9 saves have no log and load with an empty one.
    public GameEventLog EventLog {get;set;} = new();

    // Single funnel for recording a moment, so callers don't each have to
    // cope with a save whose log block is missing. `notify` also asks the
    // toast layer for a brief on-screen line.
    public GameEvent RecordEvent(GameEventKind kind, string text, bool notify = false) =>
        (EventLog ??= new GameEventLog()).Record(kind, text, notify);

    public string GetFlag(string name)
    {
        var key = FlagKey(name);
        return key != null && Flags.TryGetValue(key, out var value) ? value : null;
    }

    // What flag("x") asks: the flag holds something that isn't an explicit no.
    public bool IsFlagSet(string name) => IsFlagTruthy(GetFlag(name));

    // Setting an empty value clears the flag, so "unset" has one representation
    // and a cleared flag doesn't linger in every save file.
    public void SetFlag(string name, string value)
    {
        var key = FlagKey(name);
        if (key == null)
        {
            return;
        }
        if (string.IsNullOrEmpty(value))
        {
            Flags.Remove(key);
            return;
        }
        Flags[key] = value;
    }

    public void ClearFlag(string name) => SetFlag(name, null);

    // A value counts as set unless it explicitly says otherwise, so
    // `set_flag met_hale false` reads as "not met" rather than "met, with a
    // funny value".
    public static bool IsFlagTruthy(string value) =>
        !string.IsNullOrEmpty(value)
        && !value.Equals("false", StringComparison.OrdinalIgnoreCase)
        && value != "0";

    private static string FlagKey(string name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToLowerInvariant();
    }

    public QUESTSUCCESSSTATE GetQuestState(uint questId) =>
        Quests.FirstOrDefault(q => q.QuestId == questId)?.State ?? QUESTSUCCESSSTATE.Unstarted;

    // Get-or-create the progress record for a quest, so state changes and stage
    // edits (the in-game editor's quest commands) share one creation path.
    public QuestProgress GetOrAddQuestProgress(uint questId)
    {
        var progress = Quests.FirstOrDefault(q => q.QuestId == questId);
        if (progress == null)
        {
            progress = new QuestProgress { QuestId = questId };
            Quests.Add(progress);
        }
        return progress;
    }

    // Which stage the player is on, 0 for a quest that hasn't started or that
    // declares none (quest-system.md Phase 1).
    public uint GetQuestStage(uint questId) =>
        Quests.FirstOrDefault(q => q.QuestId == questId)?.CurrentStageNumber ?? 0;

    // The stage definition the player is on, or null when there is none.
    public QuestStage GetCurrentStage(uint questId) =>
        QuestCatalog.Get(questId)?.GetStage(GetQuestStage(questId));

    // Moves a quest to one of the stages its definition declares. Refuses a
    // stage the quest doesn't have — a mistyped `set_stage` should be a warning
    // the author sees, not a quest sitting on a beat that has no text. Returns
    // whether it moved.
    public bool SetQuestStage(uint questId, uint stageNumber)
    {
        if (QuestCatalog.Get(questId)?.GetStage(stageNumber) == null)
        {
            return false;
        }
        GetOrAddQuestProgress(questId).CurrentStageNumber = stageNumber;
        return true;
    }

    // Steps a quest to its next authored stage. False at the last one: a quest
    // ends by its state moving to Success, not by running off the end of its
    // stage list.
    public bool AdvanceQuestStage(uint questId)
    {
        var next = QuestCatalog.Get(questId)?.NextStageNumber(GetQuestStage(questId)) ?? 0;
        return next != 0 && SetQuestStage(questId, next);
    }

    public void SetQuestState(uint questId, QUESTSUCCESSSTATE state)
    {
        var progress = GetOrAddQuestProgress(questId);
        progress.State = state;
        if (state == QUESTSUCCESSSTATE.Unstarted)
        {
            // Winding a quest back drops its stage with it, so stage 0 only
            // ever means "not on one".
            progress.CurrentStageNumber = 0;
        }
        else if (state == QUESTSUCCESSSTATE.InProgress && progress.CurrentStageNumber == 0)
        {
            // A quest just taken sits on its first stage. This is also how a
            // save written before stages existed picks one up: its quests carry
            // stage 0 until something moves them.
            progress.CurrentStageNumber = QuestCatalog.Get(questId)?.FirstStageNumber ?? 0;
        }
        // Tracking follows the quest log on its own, so a player who takes a
        // quest and opens the map sees where to go without first knowing to
        // pick a row in the journal. Handled here rather than at each caller,
        // so every path a quest can move by — dialogue effects, console verbs,
        // a future QuestManager — behaves the same.
        if (state == QUESTSUCCESSSTATE.InProgress)
        {
            // Taking a quest while following none follows it; taking a second
            // one never steals the player's choice.
            if (TrackedQuestId == 0)
            {
                SetTrackedQuest(questId);
            }
            return;
        }
        if (questId == TrackedQuestId)
        {
            // The tracked quest is over: hand tracking to another quest that is
            // still in progress, or clear it when that was the last one. Either
            // way a finished quest can't leave markers on the map.
            SetTrackedQuest(NextQuestToTrack(questId));
        }
    }

    // Which quest should take over tracking: main quests before side quests
    // (the journal's own order), each in the order they were picked up. 0 when
    // nothing is in progress.
    private uint NextQuestToTrack(uint excludingQuestId)
    {
        var candidates = Quests
            .Where(progress => progress.State == QUESTSUCCESSSTATE.InProgress
                && progress.QuestId != excludingQuestId
                && QuestCatalog.Get(progress.QuestId) != null)
            .ToList();
        var next = candidates.FirstOrDefault(progress => !QuestCatalog.Get(progress.QuestId).SideQuest)
            ?? candidates.FirstOrDefault();
        return next?.QuestId ?? 0;
    }

    // The quest the player is following: its markers are what the map draws
    // (quest-markers.md Phase 3). Saved, so tracking survives a reload and is
    // available to a HUD compass later; 0 means nothing is tracked.
    public uint TrackedQuestId {get;set;}

    // Tracks a quest, or clears tracking with 0. Refuses a quest that isn't in
    // progress — the journal only offers the option for one that is, and a
    // hand-edited save shouldn't produce markers for a finished quest. Returns
    // whether the tracked quest ended up as asked.
    public bool SetTrackedQuest(uint questId)
    {
        if (questId != 0 && GetQuestState(questId) != QUESTSUCCESSSTATE.InProgress)
        {
            return false;
        }
        if (TrackedQuestId == questId)
        {
            return true;
        }
        TrackedQuestId = questId;
        QuestTracking.NotifyChanged(questId);
        return true;
    }

    public bool IsNpcDefeated(string npcId) => DefeatedNpcs.Contains(npcId);

    public void MarkNpcDefeated(string npcId)
    {
        if (!DefeatedNpcs.Contains(npcId))
        {
            DefeatedNpcs.Add(npcId);
        }
    }
}
