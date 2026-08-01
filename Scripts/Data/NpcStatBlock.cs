using Godot;

// An NPC's character sheet: the vitals and six stats a copy of them fights or
// travels with. Authored per definition (and edited in-world by the editor's
// NPC editor), it replaces the one hard-coded stat template every recruit used
// to share and gives an unauthored challenger a fight built from its own
// numbers instead of the generic brawler's.
//
// A sub-resource rather than plain fields for the same reason NpcItemStack is
// one: it renders as a single foldable block in the inspector, and Godot can't
// export the engine-free CharacterStats class directly. Definitions are shared
// cached resources — read this, never write it at runtime; ToCharacterStats
// hands out a copy.
//
// .tres files reference this script by path, so it must not move.
[GlobalClass]
public partial class NpcStatBlock : Resource
{
	[Export]
	public uint Level { get; set; } = 1;

	[Export]
	public uint MaxHealthPoints { get; set; } = 8;

	// "Power points" in battle (CharacterEntity.MaxMagicPoints).
	[Export]
	public uint MaxMagicPoints { get; set; } = 3;

	[Export]
	public uint Strength { get; set; } = 5;

	[Export]
	public uint Intelligence { get; set; } = 5;

	[Export]
	public uint Constitution { get; set; } = 5;

	[Export]
	public uint Dexterity { get; set; } = 5;

	[Export]
	public uint Wisdom { get; set; } = 5;

	[Export]
	public uint Charisma { get; set; } = 5;

	// A fresh CharacterStats for a runtime copy of this NPC (a recruited party
	// member, a battle combatant), so nothing can write back into the shared
	// definition through it.
	public CharacterStats ToCharacterStats() => new()
	{
		Strength = Strength,
		Intelligence = Intelligence,
		Constitution = Constitution,
		Dexterity = Dexterity,
		Wisdom = Wisdom,
		Charisma = Charisma,
	};
}
