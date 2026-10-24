# Dialogue State Conditions — Implementation Plan

Goal: let a conversation branch on *any* piece of current character and world state — starting with the party's credit balance — instead of the seven fixed predicates the vocabulary happens to ship with.

Status: **done** — queries and comparisons (Phase 1), conditions evaluated when a node is reached (Phase 2), and the demo conversation plus the writers' reference (Phase 3).

Depends on: [npc-dialogue-yarn.md](npc-dialogue-yarn.md) Phases 1–4 (the Yarn authoring layer, the condition/effect vocabulary, world flags) and [dialogue-editor.md](dialogue-editor.md) Phase 1 (the graph and its editor).

## The problem

A conversation can *spend* credits and cannot *ask about* them. `<<credits -200>>` is in `DialogueEffects.Ids`; there is no matching entry in `DialogueConditions.Ids`, which is the whole list:

```
quest_state, has_item, npc_defeated, party_has_room, flag, party_size, stat
```

So the obvious docking-fee conversation cannot be written:

```yarn
title: toll
---
$npc: Berth's two hundred credits. Cash up front.
-> Pay the fee <<if credits() >= 200>>     // unknown condition 'credits'
    <<credits -200>>
    <<jump paid>>
-> I'll dock elsewhere
===
```

`YarnGraphCompiler.ParseCondition` rejects it twice over: `credits` is not a known verb, and `>=` is explicitly refused before the verb is even looked at. The in-game dialogue editor can't express it either — `AddConditionEditor` builds its dropdown straight from `DialogueConditions.Ids`, so the editor is exactly as expressive as the vocabulary and no more.

The workaround — mirror the balance into a world flag with `<<set_flag rich>>` — is worse than it looks. Credits move from shops, battles, quest rewards and the `credits` console command, none of which know the flag exists, so the mirror is stale the moment the player sells anything. Duplicated state that only one writer maintains is not a gate, it's a bug with a delay.

There is a **second, quieter problem** that any credit gate will run into immediately, described under [Part 2](#part-2--evaluate-conditions-when-the-node-is-reached) below: conditions are evaluated once, when the conversation starts, so a node reached *after* `<<credits -200>>` still sees the pre-payment balance.

---

## How Yarn Spinner handles this upstream

Worth writing down properly, because the answer is "several distinct mechanisms" and we want to borrow the right one.

### 1. Variables and variable storage

Yarn has first-class variables, declared with a type-inferring initial value and assigned with `<<set>>`:

```yarn
<<declare $gold = 42>>
<<declare $player_name = "Reginald the Wizard">>
<<declare $door_unlocked = false>>
<<set $gold to $gold - 200>>
```

Three types only: Number (floating point), String, Boolean. Values live in a **variable storage** — an `IVariableStorage` implementation (`TryGetValue<T>` plus `SetValue` overloads for string/float/bool). The default `InMemoryVariableStorage` is a dictionary with JSON serialization helpers; a game that wants `$gold` to *mean* its real currency implements its own storage and forwards those reads and writes to game state. That's the documented integration point for exactly our problem, and it exists in the Godot integration too.

### 2. Operators and flow control

Conditions are ordinary expressions, with both symbolic and word-form operators:

| | |
|---|---|
| equality | `==`, `eq`, `is` |
| inequality | `!=`, `neq` |
| ordering | `>`, `<`, `>=`, `<=` (`gt`, `lt`, `gte`, `lte`) |
| boolean | `and`/`&&`, `or`/`\|\|`, `xor`/`^`, `not`/`!` |
| arithmetic | `+ - * / %`, brackets |

Used in `<<if>>` / `<<elseif>>` / `<<else>>` / `<<endif>>` blocks, in `<<set>>`, and in line interpolation (`You have {$gold} credits.`).

### 3. Functions

A condition can call a function, which must return a value and should be side-effect free — upstream is explicit that functions are *not* how you send instructions to the game, commands are. Built-ins include `visited(node)`, `visited_count(node)`, `random()`, `random_range(a,b)`, `dice(n)`, `min`/`max`, `round`, `floor`, `ceil`, `int`. Games add their own — in C# a static method tagged `[YarnFunction]`, or `dialogueRunner.AddFunction(...)`.

**This is the mechanism that matches our problem most closely.** A custom function reads live game state at the moment the condition is evaluated; nothing is mirrored, declared, or saved. `<<if credits() >= 200>>` is idiomatic upstream Yarn.

### 4. Smart variables

Yarn Spinner 3 adds read-only variables whose value is an expression evaluated on each read rather than a stored value:

```yarn
<<declare $can_afford_berth = $gold >= 200>>
```

They can't be `<<set>>`, they aren't persisted, and they recompute whenever their inputs change. They're a naming/readability layer over an expression — useful for a condition checked in twenty places, not a new source of state.

### 5. Conditional options

An option carries a trailing `<<if>>`:

```yarn
-> Pay the fee <<if $gold >= 200>>
```

Notably, a **failed condition does not remove the option**: it sets `IsAvailable = false` on the option handed to the game, and the presenter decides whether to grey it out or hide it entirely (Unity's options view has a "Show Unavailable Options" toggle). Ours always hides — see [Decisions settled](#decisions-settled).

### 6. What upstream does *not* have

No way to reach into arbitrary game objects from Yarn source. Everything crosses the boundary through one of three doors: a variable backed by storage, a function registered by the game, or a command. The Yarn side stays a small expression language over values the host chose to expose. That constraint is worth keeping.

---

## Why we're not adopting Yarn's variable model wholesale

The standing decision (npc-dialogue-yarn.md, "Decisions settled") is that Yarn is the **authoring** format and `DialogueGraph` is the **runtime**. Nothing here changes that, and the variable model is the part that fits our runtime worst:

- **`<<set $gold to ...>>` is a second write path into game state.** Save state lives in `GameState`; a variable store beside it either duplicates the balance or becomes a proxy object with no purpose. The plan already settled that dialogue state saves in `GameState`, never in a dialogue system's private storage.
- **`$variables` don't survive the graph.** `DialogueGraph` stores a condition as a flat `ConditionRef` token (`has_item:1:2`), which is what makes the in-game editor a dropdown and a text field rather than an expression editor. An arbitrary expression tree has nowhere to live in that format and nothing to render it with.
- **Yarn declarations are compile-time typed.** Supporting `<<declare>>` properly means implementing type inference and a project-wide declaration scope in `YarnParser` — a large amount of machinery for a feature whose payoff we can get from functions.

**Functions, on the other hand, are already what our conditions are.** `has_item(1)` is a call returning a bool. The gap isn't the concept; it's that every verb in the vocabulary is a *predicate with a comparison baked into it* (`party_size(2)` and `stat("Charisma", 12)` both mean "at least"), so state that isn't naturally a yes/no — a balance, a hit-point total, a level — has no way in.

The design below adds the missing half: **value-returning queries plus a real comparison operator**. That is precisely Yarn's function-plus-expression model, restricted to one comparison, and it makes our files *more* upstream-valid rather than less — `<<if credits() >= 200>>` compiles under the real Yarn compiler too, which keeps the Phase 5 "swap in the upstream compiler" door open.

---

## Design

### Part 1 — Queries and comparisons

Split the vocabulary in two, along the line Yarn already draws:

- **Predicates** return a bool and stand alone: `has_item`, `flag`, `npc_defeated`, `quest_state`, `party_has_room`, `visited`.
- **Queries** return a number or a string and must be compared: `credits`, `party_size`, `stat`, `item_count`, `health`, `level`, `quest`, `quest_stage`, `flag_value`.

A condition is then one of three shapes:

```yarn
<<if has_item(1)>>                      // predicate
<<if credits() >= 200>>                 // query compared to a literal
<<if not flag("met_hale")>>             // negated predicate
```

**New file `Scripts/Dialogue/DialogueQueries.cs`**, mirroring `DialogueConditions`' habits exactly — engine-free, total, `Ids` array for the editor, a `Validate` for author time and an `Evaluate` that warns rather than throws:

| Query | Returns | Reads |
|---|---|---|
| `credits()` | number | `GameState.Credits` |
| `party_size()` | number | `GameState.Party.Count` |
| `item_count(<itemId>)` | number | `Inventory.CountOf` |
| `stat(<name>)` | number | party leader's `CharacterStats` |
| `health()` / `max_health()` | number | leader's `HealthPoints` / `MaxHealthPoints` |
| `level()` | number | leader's `Level` |
| `quest(<questId>)` | string | `GetQuestState` name (`"InProgress"`) |
| `quest_stage(<id>)` | number | `GameState.GetQuestStage` |
| `flag_value(<name>)` | string | `GameState.GetFlag` |

Numbers compare with `== != > < >= <=`; strings with `==` / `!=`, case-insensitively, matching how `flag` compares today. Stat names stay the existing `DialogueConditions.StatNames` list, and `stat(name)` as a query supersedes `stat(name, atLeast)` as a predicate.

Queries are **pure reads**, upstream's rule for functions, and the reason they're safe to evaluate more than once per conversation ([Part 2](#part-2--evaluate-conditions-when-the-node-is-reached)).

#### Token encoding

A comparison stays a single flat `ConditionRef`, so nothing in the graph format, the JSON serializer, or the editor's data model changes. The operator and the right-hand value are the **last two args**:

| Yarn source | Token |
|---|---|
| `credits() >= 200` | `credits:>=:200` |
| `stat("Charisma") > 12` | `stat:Charisma:>:12` |
| `item_count(1) == 0` | `item_count:1:==:0` |
| `quest(1) != "Success"` | `quest:1:!=:Success` |
| `not has_item(1)` | `!has_item:1` |
| `not credits() >= 200` | `credits:<:200` |

That last row is a normalization, not a special case. Yarn's unary `not` binds
tighter than a comparison, so writing a negated comparison back out as
`not credits() >= 200` would read upstream as `(not credits()) >= 200` — a
different expression. A negated comparison is therefore stored as the inverted
operator, which says the same thing and survives the round trip through the file.
`not` on the token stays for the predicates, where it is unambiguous. For the
same reason the editor offers the `not` checkbox on predicates only: a query
inverts through its operator dropdown.

Two properties make this cheap:

- **No content migration.** A query verb with no trailing operator keeps the current "at least" meaning, so the committed `stat:Charisma:12` and `party_size:2` tokens still parse and still mean what they meant. New content should write the operator; old content doesn't have to be touched. The Yarn compiler *normalizes* the elided spelling into an explicit operator as it reads it (`stat("Charisma", 12)` → `stat:Charisma:>=:12`), so the writer has one form to emit and the round trip stays exact; the elided form keeps working wherever it is still stored.
- **No new separator problems.** `>=` and `!` contain no `:`, so `TokenRef.ToToken`/`SplitToken` are untouched, and `TokenRef.ArgFormatError` already refuses a `:` inside a free-form flag name or value.

Negation is a `!` prefix on the `Id`. `DialogueConditions.Evaluate` strips it, evaluates the rest, and inverts — which finally retires `flag("x", "")` as the way to spell "unset".

#### Parser and writer

`YarnGraphCompiler.ParseCondition` grows one branch: after the `$variable` rejection (which stays), try to split the source on a comparison operator outside of quotes. On a match, parse the left side as a call, require the right side to be a literal (number or quoted string), and emit the comparison token. Failing that, fall through to today's bare-call path.

The rejection messages for what's still unsupported get better rather than disappearing. `and` / `or` / arithmetic / `$variables` remain refused, but the message should now name the supported shape:

```
line 12: 'and' isn't supported — a condition is one call, optionally compared
to a literal (credits() >= 200) and optionally negated (not has_item(1)).
Author 'and' as nested <<if>> blocks.
```

`YarnGraphWriter.ConditionText` inverts it: a token whose trailing args are an operator and a value writes as `credits() >= 200`; a `!` prefix writes as `not has_item(1)`. The round-trip guard over every committed conversation in `Tests/YarnDialogueTests.cs` already covers this the moment content uses it.

#### Editor

`AddConditionEditor` becomes: a `not` checkbox, the verb dropdown (predicates and queries in one list, queries marked), the existing colon-args field for the call's own arguments, and — shown only when the selected verb is a query — an operator dropdown and a value field. The validator's message is what it always was, since `DialogueConditions.Validate` stays the single source of truth for both the editor and the Yarn compiler.

An author also needs to *reach* a credit-gated branch while testing; the `credits <set|add|sub> <n>` console command (`EditorItemCommands.Credits`) already does that, so unlike world flags this needs no new debug affordance.

### Part 2 — Evaluate conditions when the node is reached

`DialogueRuntime.Compile` walks the whole reachable graph up front, evaluating every router branch and every choice gate as it goes — the class comment says so plainly ("Conditions … are evaluated once, at conversation start"). That was a faithful port of what the role code did at `BuildDialogue` time, and it is fine for a greeting router that decides the branch before anyone speaks. It is wrong for anything that gates on state the same conversation just changed:

```yarn
-> Pay the fee <<if credits() >= 200>>
    <<credits -200>>
    <<jump paid>>
===
title: paid
---
<<if credits() >= 200>>            // evaluated before the payment; still true
    $npc: Still flush, I see.
<<endif>>
```

Nothing committed depends on this today — every state-based router in the intro conversations sits at the entry, and the effects fire on the way out — but it's luck, not structure, and a credits vocabulary is exactly the feature that turns it into a daily authoring trap.

**Make resolution lazy.** `DialogueLine.Next` and `DialogueLine.Choices` become properties backed by an optional resolver `Func`, populated by `DialogueRuntime` and evaluated on first access. `DialogueManager` reads `current.Next` and `line.Choices` and is otherwise untouched: the choice list is built at the moment the box renders it, and the next line at the moment the player advances.

Consequences to handle:

- The `built` memo across visits goes away — a node reached twice builds twice, which is the point. Cycles stop being a recursion hazard because nothing recurses until it is displayed, so the `routing` guard is only needed for router→router chains, which still resolve eagerly on arrival.
- Effects still fire exactly once each time their line is shown; laziness changes *when conditions are read*, never how often effects run.
- The editor's "play from here" preview (`Compile(graph, context, startNodeId)`) is unaffected.
- A router at the entry behaves identically, so the existing branch tests should pass unchanged — that's the regression bar.

### Part 3 — Content and tests

Following the shape of the phases before it:

- **A demo conversation.** Trader Moss is the natural home: a haggling branch that only appears when the player can afford it, and a follow-up line that reads the balance *after* the purchase — one script proving both halves of this plan.
- **Unit tests** per query (`Validate` + `Evaluate`, including the no-leader and empty-party cases the existing `stat` handling warns on), per operator, and for negation.
- **Compiler tests** for the new syntax and for the improved messages on what stays unsupported.
- **A writer round-trip** for every new form, plus the existing all-committed-conversations guard.
- **An ordering test** that is the whole point of Part 2: pay, then re-check, and assert the second condition sees the new balance.
- **A back-compat test** that `stat:Charisma:12` and `party_size:2` still mean "at least".

---

## Phases

### Phase 1 — Queries, comparisons, negation *(done)*

1. `Scripts/Dialogue/DialogueQueries.cs` — the nine queries in the table above, engine-free and total like the rest of the vocabulary, with `Validate` for author time and `TryNumber`/`TryText` for play time. `stat`'s name vocabulary moved here with it, since `stat` is a query now.
2. `DialogueConditions` splits into predicates and queries: `Ids` gains the query verbs so the editor and the compiler offer them, `TrySplitComparison` peels the operator and value off a token, and `Evaluate` grew a `bool?` inner result — a condition that *couldn't be evaluated* now reports "couldn't tell" rather than "false", so a negated broken gate still hides its branch instead of opening it.
3. `YarnGraphCompiler.ParseCondition` learned the comparison and `not`/`!` syntax, normalizing the elided operator and folding a negated comparison into the inverted one; `YarnGraphWriter.ConditionText` is its inverse. What stays refused is refused with a better message — `and`/`or`/`xor` and Yarn's word-form operators (`gte`) and arithmetic now name the supported shape, and the word checks only fire outside quotes, so a flag named `"this is fine"` no longer looks like Yarn's `is`.
4. The editor's condition row grew a `not` checkbox (predicates only) and, for a query, an operator dropdown and a value field; picking a verb rebuilds the row, which is how those appear.
5. **Tests:** `Tests/DialogueQueryTests.cs` (every query, every operator, negation including the broken-gate case, the legacy "at least" tokens, validation, and a credit-gated conversation played through `DialogueRuntime`), plus the new syntax, the normalizations and the new rejections in `Tests/YarnDialogueTests.cs`. `Tests/WorldFlagTests.cs` moved to the new messages and the normalized `stat` token.

**Done when:** `<<if credits() >= 200>>` gates an option and a router, in a `.yarn` file and in the in-game editor, and every existing conversation still parses and plays unchanged. ✅ — with the caveat that the editor row is the half no unit test covers.

### Phase 2 — Lazy condition evaluation *(done)*

1. `DialogueLine.Choices`/`.Next` and `DialogueChoice.Next` are backed by a small `Deferred<T>` — a value worked out the first time it is read and remembered after, which assigning the property outright replaces. `DialogueRuntime` fills the resolvers; a hand-built line (`NpcRole`, `Npc`'s role menu, a test) still assigns the values directly and behaves exactly as before.
2. `DialogueRuntime` builds only the line it is asked for. The cross-visit memo is gone, and with it the reason it existed: nothing recurses ahead of the player, so a back-edge can't loop forever — it is just a loop the conversation walks around, building a fresh line each visit so the gates on it are read again. Routers still resolve on arrival (they display nothing), which is why the router→router cycle guard stays.
3. `DialogueManager.ShowLine` runs `OnShown` **before** it reads the line's choices, instead of after. Reading the choices is what evaluates their gates now, so a gate on a line has to see what that line just did. Nothing else in the manager changed — it still reads `line.Choices` and `current.Next` exactly as it did.
4. **Tests:** the Phase 2 acceptance in `Tests/DialogueQueryTests.cs` — pay on a choice and land on a router that sees the new balance; a gate that reads what the line's own effect just gave the player; a node reached twice gated afresh each time; and effects still running once however often a line is read. `Tests/DialogueGraphTests.cs`'s shared-instance test became a walk-the-cycle test, since a back-edge is a fresh line now rather than the same one.

**Done when:** a conversation that spends credits and then branches on the balance takes the branch matching the *post-payment* state, and `Tests/IntroDialogueBranchTests.cs` passes untouched. ✅ — the intro conversations don't depend on the old timing (their state-based routers sit at the entry), so their expectations are unchanged.

One consequence worth knowing: a dangling link or an unknown verb deep in a conversation is now reported when the player *reaches* it rather than at compile, so a test that compiles a conversation and asserts no warnings only covers the first line. The graph validator (`DialogueValidation`, run over every committed conversation) is what catches those at author time.

### Phase 3 — The demo, and the docs *(done)*

1. **`intro.shopkeeper.yarn` is the demo**, as it was for world flags. Trader Moss keeps a surplus medkit under the counter for 150 credits: the offer is gated on `credits() >= 150`, so a player who can't pay never has it dangled in front of them, and the router on the far side of the payment reads the balance the purchase left behind — "there's more under here, if you're still buying" or "come back when your credits have recovered". A new game starts with 250 credits, which covers it exactly once, so both halves are visible in one playthrough: the offer is there, then it isn't.
2. **The condition syntax is documented where writers read it** — the Conventions section of [npc-dialogue-yarn.md](npc-dialogue-yarn.md) now separates predicates from queries, lists the operators each takes, says when a condition is evaluated, and explains what is still refused and how to write it instead (`and` as nested `<<if>>`, `or` as `<<elseif>>`). Phase 5's writers'-README item points at it as the thing to lift.
3. **Tests:** two cases in `Tests/IntroDialogueBranchTests.cs` walk Moss end to end — buy with 250 and get the cleaned-out line, then come back and find the option gone; buy with 400 and get the still-a-customer line. The existing all-committed-conversations guards (round trip through the writer and through JSON, and the validator) cover the new script for free.

**Done when:** an NPC visibly reacts to what's in the player's pocket, before and after they spend it. ✅ — whether it *reads* right at the counter is the half only a playthrough can answer.

---

## Deliberately out of scope

- **Compound conditions (`and` / `or`).** A router node is an ordered list of branches, which is `or`; nesting `<<if>>` blocks is `and`. Both are expressible today, and a boolean expression tree has nowhere to live in a flat `ConditionRef`. Revisit only if authored content shows the nesting getting genuinely painful.
- **Arithmetic and `$variables`.** Still rejected with a line number. Ad-hoc numeric state that isn't already in `GameState` is what the flag store is for; state the vocabulary can't reach is a missing query to add, not an expression to write.
- **Text interpolation (`You have {credits()} credits.`)** — the natural next ask, and already listed as a gap in `current-progress.md`. It needs a slot in `DialogueNode.Text` and a substitution pass; the query vocabulary designed here is the half of it that would be reusable, which is a reason to do this first.
- **Showing unaffordable options greyed out**, Yarn's `IsAvailable` behaviour. It's a nicer read for a shop ("Pay the fee — *you need 200 credits*") but it's a UI change in `DialogueManager` and a graph field, not a condition change.
- **Per-character queries beyond the leader.** `stat()`, `health()` and `level()` read the party leader, matching the existing `stat` predicate and the fact that the leader is who the player speaks as. Addressing a specific member needs a way to name one, which is a party-system question.

## Decisions settled

| Decision | Where it landed |
|---|---|
| Yarn variables vs. functions for reading game state | **Functions.** A query reads `GameState` live at evaluation time; a variable store would duplicate save state and has nowhere to live in the graph format. |
| Where the comparison lives | **In the token, as the last two args** (`credits:>=:200`), so the graph stays flat, the editor stays a form, and unoperated legacy tokens keep their "at least" meaning. |
| Yarn source syntax | **Real Yarn** — `credits() >= 200`, `not has_item(1)`. Files stay valid for the upstream toolchain and for a future swap to the upstream compiler. |
| When conditions are evaluated | **When the node is reached**, not at conversation start, so a conversation can branch on state it just changed. |
| Failed option conditions | **Still hidden**, not greyed out. Upstream hands the option over with `IsAvailable = false` and lets the presenter choose; matching that is a UI feature, tracked above as out of scope. |
| How a negation is stored | **Folded into the operator** for a comparison (`not credits() >= 200` is `credits:<:200`), kept as a `!` on the id for a predicate. Yarn's `not` binds tighter than a comparison, so the fold is what keeps a written-out file meaning what the token means. |
| Compound boolean conditions | **Not supported.** Router branch order is `or`, nested `<<if>>` is `and`. |
