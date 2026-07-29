using System;

// The clips a conversation may play on the speaking NPC (npc-dialogue-yarn.md
// Phase 4's `<<play_anim ...>>`). Engine-free like the rest of the vocabulary,
// so DialogueEffects.Validate and the editor's dropdown can check a clip name
// without touching Godot.
//
// This is a *curated* subset of the shared KayKit library
// (ThirdParty/AnimationLibrary/, ~100 clips): the ones that read as a
// conversation beat from a character standing in front of you. Swimming,
// crawling, pistol stances, and the skeleton set are all in the library and all
// nonsense here, and a wrong clip is a silent visual bug rather than an error —
// so an author picks from this list instead. Tests/DialogueAnimationTests.cs
// checks every name against the files on disk, so a typo or a removed asset
// fails the build rather than a playthrough.
//
// Adding one is a one-line change here plus a clip in the library directory.
public static class DialogueAnimations
{
    public static readonly string[] Ids =
    {
        // Neutral stand — what a one-shot returns to, and how a script ends a
        // looping gesture early.
        "Idle_A",
        // Gesturing while speaking: the default loop for a talky NPC.
        "Idle_Talking",
        // Greeting or farewell.
        "Waving",
        // Delight — a quest turn-in, a recruit accepting.
        "Cheering",
        "Dance",
        // Reaching out: fiddling with a console, pointing something out.
        "Interact",
        // Taking something off you — the fetch-quest hand-over.
        "PickUp",
        // Using or holding out an item — handing over a reward.
        "Use_Item",
        "Throw",
        // Blade out: a threat that hasn't become a battle yet.
        "Sword_Idle",
    };

    // Canonical spelling of an authored clip name, or null when it isn't one of
    // them. Case-insensitive so a script can write "waving".
    public static string Resolve(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }
        foreach (var id in Ids)
        {
            if (id.Equals(raw, StringComparison.OrdinalIgnoreCase))
            {
                return id;
            }
        }
        return null;
    }
}
