# Turn-Based Battle System

Classic JRPG-style encounters: dialogue (or later, world triggers) hands off
to a separate battle mode, the party and enemies exchange turns until one
side is wiped, and the outcome flows back into the running `GameState`.

## Phase 1 — playable core (implemented)

- **Entry point** — `BattleManager` autoload exposes
  `StartBattle(opponentName, onVictory)`. `BattleNpc` dialogue ("Vex") calls
  it; the battle begins the frame after the conversation closes.
- **Mode switch** — the scene tree is paused and the running level hidden
  (its state survives untouched); a `BattleScene` is built in code far above
  the field and its camera takes over. Victory restores the field exactly as
  it was; the winning callback lets the challenger despawn.
- **Generic themed arenas** — a flat ground plane plus a procedural-sky
  environment override on the battle camera. `BattleArenaTheme` maps the
  current world area (`GameState.LocationName`) to a theme (Station Deck,
  Desert Wastes, Verdant Fields); unknown areas fall back to Station Deck.
  Real arena scenes with props can replace the table without touching logic.
- **Turns** — rounds repeat until a side is wiped; within a round all living
  combatants act once, ordered by Dexterity (party wins ties). Party turns
  pick **Attack / Power / Item** plus a target from the `BattleHud` menus;
  enemies use a small AI (favor a damage power when affordable, else attack a
  random living party member).
- **Resources** — hit points and power points (the `MagicPoints` field on
  `CharacterEntity`, shown as PP). Powers live in `PowerCatalog` (damage /
  heal), items are the party inventory's consumables and are consumed on use.
- **Encounters** — `EnemyCatalog` maps the challenging NPC's display name to
  a `BattleEncounter` (list of `EnemyDefinition`s); unknown names get a
  generic single-enemy fight instead of crashing.
- **Outcomes** — all enemies down: victory; HP/PP are written back to the
  party (downed members revive at 1 HP), every member gains the encounter's
  XP. Whole party down: game over — the level is torn down and the player is
  sent to the Load Game menu to restore a previous save.

## Phase 2 — depth (future)

- Defend/flee commands; speed-based initiative variance
- Status effects in battle (`ActiveStatusEffect` is data-only today)
- Equipment feeding damage/defense once equip slots reference real items
- Per-character learned powers and power growth on level-up
- Enemy encounter tables per world area + random encounters
- Battle intro/outro transitions, attack animations, sound
- Real arena scenes with props per world area
