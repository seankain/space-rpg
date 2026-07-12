# Player Build System — Implementation Plan

Goal: an RPG-style character build system on top of the stat types already stubbed in `Scripts/Data/CharacterStats.cs` (Strength, Intelligence, Constitution, Dexterity, Wisdom, Charisma) and the progression fields already on `CharacterEntity` (Level, ExperiencePoints, HP/MP and their maxima).

## Where we are

- `CharacterStats` defines the six core stats but nothing reads or writes them.
- `CharacterEntity` carries `Level`, `ExperiencePoints`, `HealthPoints`/`MaxHealthPoints`, `MagicPoints`/`MaxMagicPoints`, and a `Stats` reference — all unused by gameplay.
- `NewGameMenu` has only a Start button; there is no character creation.

---

## Phase 1 — Stat rules engine

Pure C# rules, no UI. Everything lives in plain classes under `Scripts/Data/` so it serializes with saves and unit-tests without booting Godot.

1. Define **derived stats** as functions of core stats + level, in a `StatCalculator` class:
   - `MaxHealthPoints` from Constitution + level
   - `MaxMagicPoints` from Intelligence/Wisdom + level
   - Physical damage/defense modifiers from Strength/Constitution
   - Accuracy/evasion/turn-order modifiers from Dexterity (feeds turn-based combat later)
   - Dialogue/vendor modifiers from Charisma (feeds dialogue system later)
2. Define the **XP curve**: `XpRequiredForLevel(uint level)` and a `GrantXp` operation that handles multi-level-ups and returns what changed (for UI toasts later).
3. Decide the build model and document it here once chosen — recommended: **point-buy**. Each level-up grants N stat points the player allocates freely; classes/archetypes are just starting-stat presets, keeping `CharacterStats` the single source of truth.
4. Recompute-and-clamp rule: when a core stat changes, recompute derived maxima and clamp current HP/MP. One method (`StatCalculator.Refresh(CharacterEntity)`) owns this so equipment and status effects can reuse it later.

**Done when:** unit tests cover the XP curve, level-ups, and derived-stat math.

## Phase 2 — Character creation at New Game

1. Extend `NewGameMenu` with a creation panel: name entry, archetype preset picker, and a point-buy allocator over the six stats (spend/refund with a remaining-points counter).
2. On Start, build the initial `CharacterEntity` (level 1, preset + allocated stats, derived HP/MP via `StatCalculator`) and place it in `GameState.Party` — this is the same fresh-`GameState` path the save/load plan's Phase 2 creates.

**Done when:** starting a new game produces a party of one whose stats reflect creation choices, and saving/loading round-trips them.

## Phase 3 — Character sheet UI

1. Put a character sheet tab in the currently-empty `InGameMenu` (opened with Tab): name, level, XP progress bar, core stats, derived stats, HP/MP bars.
2. Level-up flow: when unspent stat points exist, show an allocation UI reusing the Phase 2 point-buy widget.
3. Surface HP/MP on `PlayerHud` (currently an empty shell).

**Done when:** the sheet reflects live `GameState` data and stat points can be spent in-game.

## Phase 4 — Hooks for other systems

Not scheduled work — contracts for systems that consume builds:

- **Combat** reads derived stats only (never raw core stats) through `StatCalculator`, so balancing is one file.
- **Equipment** (inventory plan) contributes stat modifiers via a modifier list that `Refresh` folds in — avoid mutating base `CharacterStats`.
- **Status effects** (`ActiveStatusEffect`) get a stat-modifier field and flow through the same path.
- Any new field on `CharacterEntity`/`CharacterStats` bumps `SaveVersion` per the save/load plan's Phase 5 contract.

## Decisions to settle early

| Decision | Recommendation |
|----------|----------------|
| Classes vs classless | Classless point-buy with archetype presets — cheapest to build, schema already supports it. |
| Where formulas live | One `StatCalculator` static/pure class; no formula math in nodes or UI. |
| Respec | Defer; point-buy makes it trivial to add later (refund all points). |
