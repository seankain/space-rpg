using System;

// What each character has equipped: one ItemCatalog id per slot, null when
// the slot is empty. Saves reference equipment by id only, same as Inventory,
// so definitions can be rebalanced without breaking old saves.
// The "ItemId" suffix isn't just style: pre-v4 saves serialized this class
// with enum-typed properties (LeftHand, RightHand, Chest, Legs), and plain
// slot names would rebind those stale zeros as "equipped item id 0".
public class CharacterEquipSlots
{
    public uint? HeadItemId {get;set;}
    public uint? EyesItemId {get;set;}
    public uint? LeftHandItemId {get;set;}
    public uint? RightHandItemId {get;set;}
    public uint? ChestItemId {get;set;}
    public uint? LegsItemId {get;set;}

    public uint? GetItemId(EQUIPSLOT slot) => slot switch
    {
        EQUIPSLOT.HEAD => HeadItemId,
        EQUIPSLOT.EYES => EyesItemId,
        EQUIPSLOT.LEFT_HAND => LeftHandItemId,
        EQUIPSLOT.RIGHT_HAND => RightHandItemId,
        EQUIPSLOT.CHEST => ChestItemId,
        EQUIPSLOT.LEGS => LegsItemId,
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };

    public void SetItemId(EQUIPSLOT slot, uint? itemId)
    {
        switch (slot)
        {
            case EQUIPSLOT.HEAD: HeadItemId = itemId; break;
            case EQUIPSLOT.EYES: EyesItemId = itemId; break;
            case EQUIPSLOT.LEFT_HAND: LeftHandItemId = itemId; break;
            case EQUIPSLOT.RIGHT_HAND: RightHandItemId = itemId; break;
            case EQUIPSLOT.CHEST: ChestItemId = itemId; break;
            case EQUIPSLOT.LEGS: LegsItemId = itemId; break;
            default: throw new ArgumentOutOfRangeException(nameof(slot));
        }
    }

    // Puts the item into its valid slot and returns the item id it displaced,
    // if any. The caller owns moving items in and out of the inventory.
    public uint? Equip(EquippableItem item)
    {
        var replaced = GetItemId(item.ValidEquipSlot);
        SetItemId(item.ValidEquipSlot, item.Id);
        return replaced;
    }
}
