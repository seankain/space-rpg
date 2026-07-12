# space-rpg Documentation

A 3rd-person adventure RPG built in Godot, with turn-based combat encounters and quests (planned).

| Document | Purpose |
|----------|---------|
| [current-progress.md](current-progress.md) | Snapshot of what is implemented today, how the pieces fit together, and known issues |
| [plans/save-load-system.md](plans/save-load-system.md) | Save and load system: persistence core, state capture/restore, menu integration |
| [plans/player-build-system.md](plans/player-build-system.md) | RPG player build system on the existing six-stat model: derived stats, XP/leveling, character creation |
| [plans/inventory-system.md](plans/inventory-system.md) | Player inventory for items and equipment: item catalog, pickups, equip slots, UI |
| [plans/party-system.md](plans/party-system.md) | Party system: roster, world followers, party menus, combat hooks |
| [plans/npc-system.md](plans/npc-system.md) | NPCs: interaction plumbing, placement, behaviors, persisted world state |
| [plans/npc-dialogue-yarn.md](plans/npc-dialogue-yarn.md) | NPC dialogue authored in Yarn: plugin integration, dialogue UI, game-state bridge, commands |
| [plans/quest-system.md](plans/quest-system.md) | Quests: definitions/progress split, QuestManager, journal UI, rewards |

## Suggested build order

The plans cross-reference each other; this is the dependency-friendly sequence:

1. **Save/load** — everything else stores its state in `GameState`, so the persistence contract comes first.
2. **Player build system** — stat rules and character creation; feeds character sheets, equipment deltas, and rewards.
3. **Inventory** — item catalog and equip slots; needs the build system's stat-modifier path.
4. **NPCs** — interaction plumbing (shared with pickups) and world placement.
5. **Dialogue (Yarn)** — triggered by NPC interaction; bridges to game state.
6. **Party** — roster early is cheap, but followers/recruitment shine once NPCs and dialogue exist.
7. **Quests** — sits on top of dialogue commands, inventory objectives, and build-system rewards.

Phase 1 of each plan is deliberately UI-free and unit-testable, so later systems' data models can be laid down in parallel while earlier systems' UI phases are in flight.

## Tech stack at a glance

- **Engine:** Godot 4.6 (GL Compatibility renderer, Jolt physics)
- **Language:** C# (.NET 8, `Godot.NET.Sdk/4.6.0`)
- **Assets:** Molten Maps sci-fi kit + standard Godot animation library (see `ThirdParty/`)
