using System.Collections.Generic;
using Godot;

// The bridge between an authored NpcDefinition resource and the engine-free
// NpcEditModel the in-world NPC editor edits. Kept apart from the panel so the
// resource↔form translation is one readable place, and apart from the model so
// the model stays testable without Godot.
//
// Only the fields the editor owns cross: identity, mesh, character sheet,
// wallet, and starting inventory. Placement (scene path, chunk, local
// position) belongs to EditorPlacement, and roles/behaviour waypoints are
// still authored in the Godot inspector — Apply leaves all of those alone.
public static class NpcEditMapping
{
    public static NpcEditModel ToModel(NpcDefinition definition)
    {
        var model = new NpcEditModel
        {
            NpcId = definition.NpcId ?? "",
            DisplayName = definition.DisplayName ?? "",
            RigName = EditorPlacement.RigNameOf(definition.Rig),
            RotationDegreesY = definition.RotationDegreesY,
            Credits = definition.Credits,
        };
        if (definition.Stats is { } stats)
        {
            model.Level = stats.Level;
            model.MaxHealthPoints = stats.MaxHealthPoints;
            model.MaxMagicPoints = stats.MaxMagicPoints;
            model.Stats = stats.ToCharacterStats();
        }
        foreach (var stack in definition.InitialItems ?? System.Array.Empty<NpcItemStack>())
        {
            if (stack != null)
            {
                model.Items.Add(new NpcEditItemStack(stack.ItemId, stack.Quantity));
            }
        }
        return model;
    }

    public static void Apply(NpcEditModel model, NpcDefinition definition)
    {
        definition.NpcId = model.NpcId;
        definition.DisplayName = model.DisplayName;
        definition.Rig = EditorPlacement.LoadRig(model.RigName);
        definition.RotationDegreesY = model.RotationDegreesY;
        definition.Credits = model.Credits;
        // Fresh sub-resources rather than writes into the existing ones: a
        // definition loaded from disk shares its sub-resources with anything
        // else holding them, and an unsaved edit must not leak into a live NPC.
        definition.Stats = new NpcStatBlock
        {
            Level = model.Level,
            MaxHealthPoints = model.MaxHealthPoints,
            MaxMagicPoints = model.MaxMagicPoints,
            Strength = model.Stats.Strength,
            Intelligence = model.Stats.Intelligence,
            Constitution = model.Stats.Constitution,
            Dexterity = model.Stats.Dexterity,
            Wisdom = model.Stats.Wisdom,
            Charisma = model.Stats.Charisma,
        };
        var items = new List<NpcItemStack>();
        foreach (var stack in model.Items)
        {
            items.Add(new NpcItemStack { ItemId = stack.ItemId, Quantity = stack.Quantity });
        }
        definition.InitialItems = items.ToArray();
    }
}
