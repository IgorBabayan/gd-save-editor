using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Services.GameData.Items;

/// <summary>Which items an equipment slot takes, like in the game.</summary>
public static class EquipmentSlots
{
    /// <summary>
    /// The kind the item builder lists for a new item in <paramref name="slot"/>. The off hand also takes one-handed
    /// weapons (dual wielding): its menu offers <see cref="NewItemKind.Weapon"/> as well.
    /// </summary>
    public static NewItemKind GetKind(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.Head => NewItemKind.Helm,
        EquipmentSlot.Amulet => NewItemKind.Amulet,
        EquipmentSlot.Chest => NewItemKind.ChestArmor,
        EquipmentSlot.Legs => NewItemKind.Pants,
        EquipmentSlot.Feet => NewItemKind.Boots,
        EquipmentSlot.Hands => NewItemKind.Gloves,
        EquipmentSlot.Ring1 or EquipmentSlot.Ring2 => NewItemKind.Ring,
        EquipmentSlot.Waist => NewItemKind.Belt,
        EquipmentSlot.Shoulders => NewItemKind.Shoulders,
        EquipmentSlot.Medal => NewItemKind.Medal,
        EquipmentSlot.Relic => NewItemKind.Relic,
        EquipmentSlot.MainHand => NewItemKind.Weapon,
        EquipmentSlot.OffHand => NewItemKind.OffHand,
        _ => NewItemKind.Other
    };

    /// <summary>Kinds offered when building a new item for <paramref name="slot"/>, the usual one first.</summary>
    public static IReadOnlyList<NewItemKind> GetKinds(EquipmentSlot slot) =>
        slot == EquipmentSlot.OffHand ? [NewItemKind.OffHand, NewItemKind.Weapon] : [GetKind(slot)];

    /// <summary>
    /// An item of class <paramref name="cls"/> can be worn in <paramref name="slot"/>. The off hand takes shields,
    /// off-hands and one-handed weapons; two-handed weapons go in the main hand only.
    /// </summary>
    public static bool Accepts(EquipmentSlot slot, string? cls)
    {
        cls ??= string.Empty;
        return slot switch
        {
            EquipmentSlot.Head => cls == "ArmorProtective_Head",
            EquipmentSlot.Amulet => cls == "ArmorJewelry_Amulet",
            EquipmentSlot.Chest => cls == "ArmorProtective_Chest",
            EquipmentSlot.Legs => cls == "ArmorProtective_Legs",
            EquipmentSlot.Feet => cls == "ArmorProtective_Feet",
            EquipmentSlot.Hands => cls == "ArmorProtective_Hands",
            EquipmentSlot.Ring1 or EquipmentSlot.Ring2 => cls == "ArmorJewelry_Ring",
            EquipmentSlot.Waist => cls == "ArmorProtective_Waist",
            EquipmentSlot.Shoulders => cls == "ArmorProtective_Shoulders",
            EquipmentSlot.Medal => cls == "ArmorJewelry_Medal",
            EquipmentSlot.Relic => cls == "ItemArtifact",
            EquipmentSlot.MainHand => IsWeapon(cls),
            EquipmentSlot.OffHand => cls is "WeaponArmor_Shield" or "WeaponArmor_Offhand" || (IsWeapon(cls) && !IsTwoHanded(cls)),
            _ => false
        };
    }

    /// <summary>A two-handed weapon: nothing can be held in the off hand with it.</summary>
    public static bool IsTwoHanded(string? cls) =>
        cls is "WeaponMelee_Axe2h" or "WeaponMelee_Mace2h" or "WeaponMelee_Sword2h" or "WeaponHunting_Ranged2h"
            or "WeaponHunting_Spear" or "WeaponMagical_Staff";

    private static bool IsWeapon(string cls) =>
        cls.StartsWith("WeaponMelee", StringComparison.OrdinalIgnoreCase)
        || cls.StartsWith("WeaponHunting", StringComparison.OrdinalIgnoreCase)
        || cls.StartsWith("WeaponMagical", StringComparison.OrdinalIgnoreCase);
}
