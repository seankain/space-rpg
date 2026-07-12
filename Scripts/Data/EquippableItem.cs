public enum EQUIPSLOT
{
    HEAD,
    EYES,
    LEFT_HAND,
    RIGHT_HAND,
    CHEST,
    LEGS
}
public abstract class EquippableItem : Item
{
    public EQUIPSLOT ValidEquipSlot{get;set;}

    protected EquippableItem()
    {
        MaxStackSize = 1;
    }
}

public class Weapon : EquippableItem
{
    public override ItemCategory Category => ItemCategory.Weapon;
    public uint PhysicalDamage {get;set;}
}

public class Armor : EquippableItem
{
    public override ItemCategory Category => ItemCategory.Armor;
    public uint PhysicalDefense {get;set;}
}
