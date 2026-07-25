using Godot;

// Base of the composable NPC interaction roles (docs/plans/npc-composition.md
// Phase 2). A definition's Roles array replaces the old one-subclass-per-role
// scene variants: each role contributes a conversation plus world actions,
// and Npc composes whatever roles are available at interact time — which is
// how one NPC can be a quest giver AND a recruit, or gate one role behind
// another's quest.
//
// Roles are authored inside NpcDefinition .tres files, so they come from
// Godot's shared resource cache: treat them as immutable templates. Anything
// mutable a role needs at runtime (a shop's Merchant) is created through
// CreateRuntimeState and owned by the Npc node, never written back here.
//
// .tres files reference this script by path, so it must not move.
[GlobalClass]
public partial class NpcRole : Resource
{
	// The conversation this role plays, by DialogueGraph id (the file stem
	// under Resources/Dialogue). Dialogue is data now (dialogue-editor plan
	// Phase 2): the role names a graph, DialogueCatalog loads it, and
	// DialogueRuntime compiles it into the runtime tree — no C# tree building.
	// Empty falls back to small talk.
	[Export]
	public string DialogueId { get; set; } = "";

	// Availability gate usable by any role: when RequiredQuestId is a real
	// quest (non-zero), the role only joins the conversation while that
	// quest sits in RequiredQuestState — e.g. a recruit offer that appears
	// once the NPC's own quest hits Success. Zero means always available.
	[Export]
	public uint RequiredQuestId { get; set; }

	[Export]
	public QUESTSUCCESSSTATE RequiredQuestState { get; set; } = QUESTSUCCESSSTATE.Success;

	// Veto instantiating the NPC at all (already recruited, defeated-and-
	// despawns). Runs before the Npc node exists; state may be null when no
	// save is loaded, which should read as "spawn".
	public virtual bool ShouldSpawn(NpcDefinition definition, GameState state) => true;

	public virtual bool IsAvailable(Npc npc, GameState state) =>
		RequiredQuestId == 0 || state.GetQuestState(RequiredQuestId) == RequiredQuestState;

	// Per-NPC mutable state, created on the Npc's _Ready and fetched back at
	// play time via npc.GetRoleState (a shopkeeper's Merchant, read by the
	// open_shop effect host). Null for stateless roles.
	public virtual object CreateRuntimeState(Npc npc) => null;

	// Choice label when this role shares the NPC with other available roles;
	// a lone role skips the menu and plays its dialogue directly.
	public virtual string MenuLabel => "Just talking";

	// The role's conversation, compiled from its DialogueId graph against the
	// live NPC and game state. state is never null (Npc checks first). Roles no
	// longer build DialogueLines in code; the branching, side effects, and
	// gating all live in the graph's routers, effects, and conditions.
	public virtual DialogueLine BuildDialogue(Npc npc, GameState state)
	{
		if (string.IsNullOrEmpty(DialogueId))
		{
			return SmallTalk(npc);
		}
		var graph = DialogueCatalog.Get(DialogueId);
		if (graph == null)
		{
			GD.PushWarning($"Role on '{npc.DisplayName}' names unknown dialogue '{DialogueId}'.");
			return SmallTalk(npc);
		}
		return DialogueRuntime.Compile(graph, npc.CreateDialogueContext(state));
	}

	private static DialogueLine SmallTalk(Npc npc) =>
		new DialogueLine { Speaker = npc.DisplayName, Text = "Hello there." };
}
