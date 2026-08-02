using System;
using System.Collections.Generic;

// The runtime conversation tree DialogueManager walks, engine-free like
// Scripts/Data. Lines are no longer hand-built in C#: DialogueRuntime compiles
// them from a serialized DialogueGraph (docs/plans/dialogue-editor.md), and a
// .yarn file is one way to author that graph.
//
// Choices and the following line are **resolved on first read** rather than
// worked out when the conversation is compiled (docs/plans/dialogue-state-conditions.md
// Phase 2). That is what lets a gate see state the conversation itself has
// changed: the choice list is built when the box renders the line, and the next
// line when the player advances — after the picked choice's effects have run.
// Each resolves once per line and is remembered, so the box and the input
// handler can read it repeatedly and always agree.
public class DialogueLine
{
    // Who is talking; null/empty renders as unattributed narration.
    public string Speaker { get; set; }
    public string Text { get; set; }
    // Invoked when the line is displayed — used for side effects that should
    // happen as the line plays (take an item, complete a quest). Runs before
    // the line's choices are read, so a gate can react to what it changed.
    public Action OnShown { get; set; }

    private readonly Deferred<List<DialogueChoice>> choices = new();
    private readonly Deferred<DialogueLine> next = new();

    // When set, the line ends in a choice instead of a continue prompt.
    public List<DialogueChoice> Choices
    {
        get => choices.Value;
        set => choices.Value = value;
    }

    // Next line when there are no choices; null ends the conversation.
    public DialogueLine Next
    {
        get => next.Value;
        set => next.Value = value;
    }

    public void ResolveChoices(Func<List<DialogueChoice>> resolver) => choices.Resolve(resolver);

    public void ResolveNext(Func<DialogueLine> resolver) => next.Resolve(resolver);
}

public class DialogueChoice
{
    public string Label { get; set; }
    // Invoked when the choice is picked, before Next is read.
    public Action Action { get; set; }

    private readonly Deferred<DialogueLine> next = new();

    // Line shown after picking; null ends the conversation.
    public DialogueLine Next
    {
        get => next.Value;
        set => next.Value = value;
    }

    public void ResolveNext(Func<DialogueLine> resolver) => next.Resolve(resolver);
}

// A value worked out the first time it is asked for and remembered after.
// Assigning the value outright — a hand-built line in NpcRole or a test — drops
// any resolver, so the two ways of filling a line never fight.
internal sealed class Deferred<T> where T : class
{
    private Func<T> resolver;
    private T value;

    public T Value
    {
        get
        {
            if (resolver is { } resolve)
            {
                // Cleared first: a resolver that reads its own property gets the
                // unresolved value rather than recursing forever.
                resolver = null;
                value = resolve();
            }
            return value;
        }
        set
        {
            resolver = null;
            this.value = value;
        }
    }

    public void Resolve(Func<T> resolve)
    {
        resolver = resolve;
        value = null;
    }
}
