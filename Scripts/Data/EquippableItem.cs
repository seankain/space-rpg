public enum EQUIPSLOT
{
    HEAD,
    EYES,
    LEFT_HAND,
    RIGHT_HAND,
    CHEST,
    LEGS
}
public class EquippableItem : Item
{
    public EQUIPSLOT ValidEquipSlot{get;set;}
}

public class Weapon : EquippableItem
{
    public uint PhysicalDamage {get;set;}
}

public class Armor : EquippableItem
{
    public uint PhysicalDefense {get;set;}
}