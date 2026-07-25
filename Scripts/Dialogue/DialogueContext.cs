using System;

// Everything a compiled conversation needs at play time, kept engine-free so
// DialogueEffects/DialogueConditions and their tests never touch Godot
// (dialogue-editor plan Phase 1). The pure verbs (give_item, set_quest, …)
// mutate State directly; the scene verbs (start_battle, open_shop, recruit)
// go through Host, whose real implementation bridges to the live Npc/scene and
// is wired up during the Phase 2 migration.
public class DialogueContext
{
    // The conversation runs against this state; effects mutate it and
    // conditions read it. Never null when a conversation is playing.
    public GameState State { get; set; }

    // Display name substituted wherever a node's Speaker is
    // DialogueGraph.SpeakerToken, so authored data can name "the NPC" without
    // hard-coding a character.
    public string SpeakerName { get; set; }

    // Scene-facing effects delegate here (see IDialogueEffectHost). May be null
    // in tests or headless contexts that only exercise the pure verbs; the
    // scene verbs no-op with a warning when it is.
    public IDialogueEffectHost Host { get; set; }

    // Where a runtime problem (dangling link, unknown verb, bad arg) is
    // reported instead of crashing the conversation. Defaults to a no-op.
    public Action<string> LogWarning { get; set; } = _ => { };

    public void Warn(string message) => LogWarning?.Invoke(message);
}

// The scene-touching side effects a conversation can trigger. Implemented by a
// Godot bridge over the live Npc (Phase 2); a test fake records the calls. The
// named vocabulary is fixed, so authored data can only ever pick from it.
public interface IDialogueEffectHost
{
    // start_battle: begins a turn-based fight with the speaking NPC once the
    // conversation closes (DialogueActions.StartBattle today).
    void StartBattle();

    // open_shop: opens the trading screen for a shopkeeper NPC.
    void OpenShop();

    // recruit:<partyCharacterId>: adds the speaking NPC to the party as the
    // given character id and hands off the world body to a follower.
    void Recruit(ulong partyCharacterId);
}
