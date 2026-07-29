using System;

// The effect host for the editor's "play from here" preview (dialogue-editor
// plan Phase 5). Pure verbs (give_item, set_quest, …) still run through
// DialogueEffects against the live GameState — that is the point of previewing
// against the running state — but the scene verbs are intercepted and only
// logged, so iterating on a conversation never actually launches a battle,
// opens the shop, or recruits an NPC into the party.
public sealed class DialoguePreviewHost : IDialogueEffectHost
{
	private readonly Action<string> log;

	public DialoguePreviewHost(Action<string> log)
	{
		this.log = log ?? (_ => { });
	}

	public void StartBattle(bool despawnOnDefeat) =>
		log($"[preview] start_battle{(despawnOnDefeat ? " (despawn)" : "")} — skipped in preview.");

	public void OpenShop() => log("[preview] open_shop — skipped in preview.");

	public void Recruit(ulong partyCharacterId) =>
		log($"[preview] recruit:{partyCharacterId} — skipped in preview.");

	// A gesture is harmless, but a preview has no speaking NPC to play it on
	// (it runs a graph by id, not a conversation with somebody), so it is
	// reported like the rest.
	public void PlayAnimation(string clip, bool loop) =>
		log($"[preview] play_anim:{clip}{(loop ? ":loop" : "")} — no NPC in preview.");
}
