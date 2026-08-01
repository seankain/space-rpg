using System;
using System.Collections.Generic;
using System.Linq;

// One starting-inventory row in the NPC editor. The Godot-facing
// NpcItemStack resource this maps to lives in Scripts/Data; this is its
// engine-free twin so the add/merge/validate rules can be tested.
public sealed class NpcEditItemStack
{
    public NpcEditItemStack(uint itemId, uint quantity)
    {
        ItemId = itemId;
        Quantity = quantity;
    }

    public uint ItemId { get; set; }
    public uint Quantity { get; set; }
}

// One role's conversation as the NPC editor shows it: which role it belongs to
// and the DialogueGraph id that role plays. The Godot-facing NpcRole resource
// this maps to lives in Scripts/Npc/Roles; this is its engine-free twin, so the
// rules about what a conversation may be called are testable.
public sealed class NpcDialogueLink
{
    public NpcDialogueLink(string roleLabel, string dialogueId)
    {
        RoleLabel = roleLabel ?? "";
        DialogueId = dialogueId ?? "";
    }

    // How the row is titled — the role's kind ("QuestGiver", "Talk").
    public string RoleLabel { get; }

    // The conversation the role plays, by DialogueGraph id. Empty means the
    // role falls back to small talk.
    public string DialogueId { get; set; }

    // True for a row the author added in the editor: the definition has no such
    // role yet, so saving creates one.
    public bool IsNewRole { get; init; }
}

// The editable form behind the in-world NPC editor (NpcEditorPanel): every
// field the author can change on an NPC, plus the rules for whether the
// result is savable. Engine-free on purpose — NpcEditorPanel maps an
// NpcDefinition resource onto one of these, edits it, and maps it back, so
// the validation that decides whether a .tres gets written is unit-tested
// without a running game.
public sealed class NpcEditModel
{
    // Filesystem-hostile characters, plus the ones Godot's resource paths use.
    // An NPC id becomes <id>.tres, so it has to survive being a file name.
    private static readonly char[] IllegalIdChars =
        { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };

    // Empty means "no rig" — the placeholder capsule tinted by BodyColor.
    public const string NoRig = "";

    public string NpcId { get; set; } = "";
    public string DisplayName { get; set; } = "";

    // Rig wrapper scene basename (Scenes/Characters/Rigs/<name>.tscn), or
    // NoRig for the capsule.
    public string RigName { get; set; } = NoRig;

    public float RotationDegreesY { get; set; }

    // Vitals the recruit/battle paths read off the definition. Level 1 with a
    // small pool matches the template recruits used to be hard-coded with.
    public uint Level { get; set; } = 1;
    public uint MaxHealthPoints { get; set; } = 8;
    public uint MaxMagicPoints { get; set; } = 3;

    public CharacterStats Stats { get; set; } = new()
    {
        Strength = 5,
        Intelligence = 5,
        Constitution = 5,
        Dexterity = 5,
        Wisdom = 5,
        Charisma = 5,
    };

    public uint Credits { get; set; }

    public List<NpcEditItemStack> Items { get; } = new();

    // The NPC's conversations, one row per role, in role order — what the
    // editor's Conversations section lists and what its "Edit dialogue" button
    // opens. Rows added here beyond the definition's roles become plain talking
    // roles when the .tres is written (NpcEditMapping.Apply).
    public List<NpcDialogueLink> Dialogues { get; } = new();

    // Appends a row for a role this NPC doesn't have yet — the "+ conversation"
    // button, which is how a freshly placed NPC (no roles at all) gets one.
    public NpcDialogueLink AddDialogueRole(string dialogueId)
    {
        var link = new NpcDialogueLink("Talk", dialogueId) { IsNewRole = true };
        Dialogues.Add(link);
        return link;
    }

    // A free conversation id for a new dialogue on this NPC: the NPC's own id,
    // then <id>.2, <id>.3 — a conversation is written as <id>.yarn, so naming
    // it after the NPC keeps the file next to the ones the game ships.
    // `isTaken` answers "does a conversation already use this id?"; the panel
    // asks the catalog and the rows already on the form.
    public string SuggestDialogueId(Func<string, bool> isTaken)
    {
        var baseId = string.IsNullOrWhiteSpace(NpcId) ? "conversation" : NpcId;
        if (isTaken == null || !isTaken(baseId))
        {
            return baseId;
        }
        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{baseId}.{suffix}";
            if (!isTaken(candidate))
            {
                return candidate;
            }
        }
    }

    // Adds a stack, folding into the row that already carries the item so the
    // list can't grow two rows of Medkits that disagree.
    public void AddItem(uint itemId, uint quantity)
    {
        if (quantity == 0)
        {
            quantity = 1;
        }
        if (Items.FirstOrDefault(s => s.ItemId == itemId) is { } existing)
        {
            existing.Quantity += quantity;
            return;
        }
        Items.Add(new NpcEditItemStack(itemId, quantity));
    }

    public void RemoveItem(int index)
    {
        if (index >= 0 && index < Items.Count)
        {
            Items.RemoveAt(index);
        }
    }

    // Every reason this NPC can't be saved, in the order the form shows them;
    // empty means the .tres can be written. `isIdTaken` answers "does another
    // NPC already own this id?" — the caller (the panel) asks NpcDatabase and
    // excludes the NPC being edited, so renaming to your own id is fine.
    public IReadOnlyList<string> Validate(Func<string, bool> isIdTaken)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(NpcId))
        {
            problems.Add("NPC id must not be empty.");
        }
        else if (NpcId.Any(char.IsWhiteSpace))
        {
            problems.Add("NPC id must not contain spaces — it becomes a file name.");
        }
        else if (NpcId.IndexOfAny(IllegalIdChars) >= 0)
        {
            problems.Add($"NPC id must not contain any of {new string(IllegalIdChars)}.");
        }
        else if (isIdTaken != null && isIdTaken(NpcId))
        {
            problems.Add($"An NPC with id '{NpcId}' already exists. Pick a unique id.");
        }

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            problems.Add("Display name must not be empty.");
        }
        if (MaxHealthPoints == 0)
        {
            problems.Add("Max health must be at least 1.");
        }

        for (var i = 0; i < Items.Count; i++)
        {
            var stack = Items[i];
            if (ItemCatalog.Get(stack.ItemId) == null)
            {
                problems.Add($"Item row {i + 1}: no item with id {stack.ItemId}.");
            }
            if (stack.Quantity == 0)
            {
                problems.Add($"Item row {i + 1}: quantity must be at least 1.");
            }
        }

        for (var i = 0; i < Dialogues.Count; i++)
        {
            // A conversation id becomes <id>.yarn, so it lives under the same
            // file-name rules as an NPC id. Empty is fine — that role just falls
            // back to small talk.
            var id = Dialogues[i].DialogueId;
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }
            if (id.Any(char.IsWhiteSpace))
            {
                problems.Add($"Conversation row {i + 1}: dialogue id must not contain spaces.");
            }
            else if (id.IndexOfAny(IllegalIdChars) >= 0)
            {
                problems.Add($"Conversation row {i + 1}: dialogue id must not contain any of {new string(IllegalIdChars)}.");
            }
        }
        return problems;
    }

    // The stat block a recruited/fighting copy of this NPC starts with.
    // Returned fresh so callers can't write back into the shared definition.
    public CharacterStats CopyStats() => new()
    {
        Strength = Stats.Strength,
        Intelligence = Stats.Intelligence,
        Constitution = Stats.Constitution,
        Dexterity = Stats.Dexterity,
        Wisdom = Stats.Wisdom,
        Charisma = Stats.Charisma,
    };
}
