using System.Collections.Generic;
using Godot;

// The bridge between an authored NpcDefinition resource and the engine-free
// NpcEditModel the in-world NPC editor edits. Kept apart from the panel so the
// resource↔form translation is one readable place, and apart from the model so
// the model stays testable without Godot.
//
// Only the fields the editor owns cross: identity, mesh, character sheet,
// wallet, starting inventory, and each role's conversation id. Placement (scene
// path, chunk, local position) belongs to EditorPlacement, and everything else
// about a role — the kind of role it is, its quest gate, patrol waypoints — is
// still authored in the Godot inspector; Apply leaves all of that alone.
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
        // One conversation row per role, in role order — the order the rows map
        // back by, and the order the in-game choice menu shows them in.
        foreach (var role in definition.Roles ?? System.Array.Empty<NpcRole>())
        {
            model.Dialogues.Add(new NpcDialogueLink(RoleLabel(role), role?.DialogueId));
        }
        return model;
    }

    // How a conversation row is titled: the role's kind, minus the noise. The
    // base NpcRole is the plain conversationalist, so it reads as "Talk".
    private static string RoleLabel(NpcRole role)
    {
        if (role == null)
        {
            return "(empty role)";
        }
        var name = role.GetType().Name;
        if (name == nameof(NpcRole))
        {
            return "Talk";
        }
        return name.EndsWith("Role") ? name[..^"Role".Length] : name;
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
        ApplyDialogues(model, definition);
    }

    // Writes each conversation row back onto the role it came from, and turns a
    // row the author added into a plain talking role. Unlike the stat block,
    // the existing roles are written in place rather than replaced: a role is a
    // whole authored resource (a shop's stock, a quest gate) that the editor
    // only knows one field of — the same in-place write `dialogue assign` does.
    //
    // Idempotent by construction: rows and roles line up by index, so a second
    // Apply re-sets ids instead of appending the new roles again.
    private static void ApplyDialogues(NpcEditModel model, NpcDefinition definition)
    {
        var roles = new List<NpcRole>(definition.Roles ?? System.Array.Empty<NpcRole>());
        for (var i = 0; i < model.Dialogues.Count; i++)
        {
            var dialogueId = model.Dialogues[i].DialogueId ?? "";
            if (i < roles.Count)
            {
                if (roles[i] != null)
                {
                    roles[i].DialogueId = dialogueId;
                }
            }
            else
            {
                roles.Add(new NpcRole { DialogueId = dialogueId });
            }
        }
        definition.Roles = roles.ToArray();
    }
}
