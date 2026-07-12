using System;
using System.Collections.Generic;

// Minimal hand-authored dialogue tree, engine-free like Scripts/Data. This is
// the placeholder conversation format until Yarn Spinner lands (see
// docs/plans/npc-dialogue-yarn.md); NPC scripts build these trees in code.
public class DialogueLine
{
    // Who is talking; null/empty renders as unattributed narration.
    public string Speaker { get; set; }
    public string Text { get; set; }
    // Invoked when the line is displayed — used for side effects that should
    // happen as the line plays (take an item, complete a quest).
    public Action OnShown { get; set; }
    // When set, the line ends in a choice instead of a continue prompt.
    public List<DialogueChoice> Choices { get; set; }
    // Next line when there are no choices; null ends the conversation.
    public DialogueLine Next { get; set; }
}

public class DialogueChoice
{
    public string Label { get; set; }
    // Invoked when the choice is picked, before Next is shown.
    public Action Action { get; set; }
    // Line shown after picking; null ends the conversation.
    public DialogueLine Next { get; set; }
}
