# Inventory System — Implementation Plan

Goal: a player inventory for items and equipment, built on the existing item hierarchy (`Item`, `EquippableItem`, `Weapon`, `Armor` in `Scripts/Data/`) and equip-slot definitions (`EQUIPSLOT`, `CharacterEquipSlots`).

## Where we are

- `Item` has id/name/description; `EquippableItem` adds a valid slot; `Weapon`/`Armor` add damage/defense. Nothing instantiates them.
- `CharacterEquipSlots` currently types each slot as the `EQUIPSLOT` **enum** rather than a reference to an equipped item — this needs to change (each slot should hold an equipped item id, or nothing).
- The `Inventory` input action (Tab) already exists and opens the empty `InGameMenu`.

---

## Phase 1 — Item catalog and inventory data model

1. **Item catalog:** item *definitions* live in game data (a JSON catalog under `res://Data/items.json`, or C# registry to start), keyed by `Item.Id`. Runtime state and saves reference items **by id only** — saves stay small and item stat rebalances apply to old saves retroactively. Add an `ItemCatalog` loader with lookup by id.
2. **Inventory model:** `Inventory` class holding `List<ItemStack>` (`ItemId`, `Quantity`), with add/remove/count/stacking operations. Decide capacity model (recommend: unlimited slots, stack caps per item definition — simplest, no encumbrance math).
3. **Fix `CharacterEquipSlots`:** replace the enum-typed properties with nullable equipped item ids per slot (`uint? Head`, `uint? RightHand`, …). Equip/unequip operations validate `EquippableItem.ValidEquipSlot` and move items between inventory and slots atomically.
4. Add `Inventory` to `GameState` (party-shared inventory — see party plan) and bump `SaveVersion` with a migration per the save/load plan.

**Done when:** unit tests cover stacking, equip/unequip validation, and catalog lookup; inventory round-trips through save/load.

## Phase 2 — Acquiring items in the world

1. **Pickup scene:** a `Pickup` node (Area3D + mesh) carrying an item id + quantity; the existing `Interact` action (E) collects it into the inventory. Show a brief "picked up X" HUD toast.
2. **Persistence of collected pickups:** collected pickup ids recorded in world state (keyed per level/chunk — `CharacterEntity.ChunkId` anticipates this) so loading a save doesn't respawn them. This is the first entry in the save/load plan's Phase 5 "world state" bucket.
3. Author a few test items in the catalog and scatter pickups in the Intro level.

**Done when:** items collected in the Intro level survive save/quit/load and don't reappear.

## Phase 3 — Inventory and equipment UI

1. Inventory tab in `InGameMenu`: grid or list of stacks with name, quantity, description panel.
2. Equipment pane: the six `EQUIPSLOT` slots per character with equip/unequip via the Phase 1 operations; show stat deltas (via the build plan's `StatCalculator` modifier path) before confirming.
3. Item context actions: equip, drop (spawns a `Pickup`), and a stub "use" for future consumables.

**Done when:** a picked-up weapon can be equipped from the UI and its stats show on the character sheet.

## Phase 4 — Depth (as needed by combat/quests)

- **Consumables:** `UsableItem` subtype with effects (heal, cure status) — needed by turn-based combat.
- **Key/quest items:** non-droppable flag; quest plan consumes this.
- **Loot tables:** drops from combat encounters.
- **Vendors:** buy/sell using a `Currency` field on `GameState`; Charisma modifier hook from the build plan.

## Decisions to settle early

| Decision | Recommendation |
|----------|----------------|
| Per-character vs party inventory | One shared party inventory (classic JRPG style) — matches the party plan and avoids transfer UI. Equipment is per-character. |
| Item definition format | JSON catalog loaded at boot; avoid Godot `Resource` files for definitions that saves reference, keeping the data model engine-free like the rest of `Scripts/Data/`. |
| Item id space | Keep `uint` ids but assign them in the catalog file; never reuse ids once shipped (saves reference them). |
