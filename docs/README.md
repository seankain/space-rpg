# space-rpg Documentation

A 3rd-person adventure RPG built in Godot, with turn-based combat encounters and quests (planned).

| Document | Purpose |
|----------|---------|
| [current-progress.md](current-progress.md) | Snapshot of what is implemented today, how the pieces fit together, and known issues |
| [plans/save-load-system.md](plans/save-load-system.md) | Phased implementation plan for the save and load system |

## Tech stack at a glance

- **Engine:** Godot 4.6 (GL Compatibility renderer, Jolt physics)
- **Language:** C# (.NET 8, `Godot.NET.Sdk/4.6.0`)
- **Assets:** Molten Maps sci-fi kit + standard Godot animation library (see `ThirdParty/`)
