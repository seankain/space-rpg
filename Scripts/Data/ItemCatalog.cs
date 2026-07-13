using System.Collections.Generic;

// All item definitions, keyed by Item.Id. Saves and runtime state reference
// items by id only, so definitions can be rebalanced without breaking old
// saves. A C# registry to start; can move to a JSON catalog later without
// changing callers. Ids are permanent once shipped — never reuse them.
public static class ItemCatalog
{
    public const uint MaguffinCubeId = 1;
    public const uint PlasmaCutterId = 2;
    public const uint VacSuitPlatingId = 3;
    public const uint MedkitId = 4;
    public const uint MaintenanceKeycardId = 5;

    private static readonly Dictionary<uint, Item> items = new();

    static ItemCatalog()
    {
        Register(new QuestItem
        {
            Id = MaguffinCubeId,
            Name = "Maguffin Cube",
            Description = "A perfectly smooth cube that hums faintly. Everyone seems to want it, though nobody can say what it does.",
        });
        Register(new Weapon
        {
            Id = PlasmaCutterId,
            Name = "Plasma Cutter",
            Description = "A repurposed mining tool. The safety interlock was removed a long time ago.",
            ValidEquipSlot = EQUIPSLOT.RIGHT_HAND,
            PhysicalDamage = 3,
            Value = 120,
        });
        Register(new Armor
        {
            Id = VacSuitPlatingId,
            Name = "Vac-Suit Plating",
            Description = "Bolt-on chest plating rated for micrometeorites and small-arms fire.",
            ValidEquipSlot = EQUIPSLOT.CHEST,
            PhysicalDefense = 2,
            Value = 90,
        });
        Register(new ConsumableItem
        {
            Id = MedkitId,
            Name = "Medkit",
            Description = "A sealed trauma kit. Restores a modest amount of health.",
            HealAmount = 5,
            Value = 25,
        });
        Register(new QuestItem
        {
            Id = MaintenanceKeycardId,
            Name = "Maintenance Keycard",
            Description = "A scuffed station keycard coded for the maintenance decks. Security only hands these out to people they owe.",
        });
    }

    private static void Register(Item item)
    {
        items.Add(item.Id, item);
    }

    public static Item Get(uint id) => items.TryGetValue(id, out var item) ? item : null;

    public static IReadOnlyCollection<Item> All => items.Values;
}
