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

	// Per-NPC mutable state, created on the Npc's _Ready and fetched back in
	// BuildDialogue via npc.GetRoleState. Null for stateless roles.
	public virtual object CreateRuntimeState(Npc npc) => null;

	// Choice label when this role shares the NPC with other available roles;
	// a lone role skips the menu and plays its dialogue directly.
	public virtual string MenuLabel => "Just talking";

	// The role's conversation. state is never null (Npc checks first).
	public virtual DialogueLine BuildDialogue(Npc npc, GameState state) =>
		new DialogueLine { Speaker = npc.DisplayName, Text = "Hello there." };
}
