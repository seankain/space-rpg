// The inventory UI groups items into these sections; every concrete item
// type maps to exactly one of them.
public enum ItemCategory
{
    Weapon,
    Armor,
    Consumable,
    QuestItem,
}

// Item definitions live in the ItemCatalog and are referenced by id from
// runtime state and saves; Item instances themselves are never serialized.
public abstract class Item
{
    public uint Id{get;set;}
    public string Name {get;set;}
    public string Description {get;set;}
    public abstract ItemCategory Category {get;}
    // How many of this item a single inventory stack can hold.
    public uint MaxStackSize {get;set;} = 99;
    // Base worth in credits: merchants sell at this and buy back at a
    // discount (see Trade). 0 means the item can't be traded.
    public uint Value {get;set;}
}

// Healing items and other one-shot usables.
public class ConsumableItem : Item
{
    public override ItemCategory Category => ItemCategory.Consumable;
    public uint HealAmount {get;set;}
}

// Story-critical items; can't be discarded once the drop action exists.
public class QuestItem : Item
{
    public override ItemCategory Category => ItemCategory.QuestItem;

    public QuestItem()
    {
        MaxStackSize = 1;
    }
}
