namespace GD.SaveEditor.Services.GameData.Items;

/// <summary>What kind of thing an item record is, which decides what the item builder can add to it.</summary>
public enum ItemCategory
{
    /// <summary>Armor, jewelry and weapons: prefix, suffix, crafting bonus, component and augment.</summary>
    Gear,

    /// <summary>Relics (<c>ItemArtifact</c>): a completion bonus and a crafting bonus.</summary>
    Relic,

    /// <summary>Components (<c>ItemRelic</c>), stackable.</summary>
    Component,

    /// <summary>Augments (<c>ItemEnchantment</c>), stackable.</summary>
    Augment,

    /// <summary>Potions, scrolls and other consumables (<c>OneShot_*</c>), stackable.</summary>
    Consumable,

    /// <summary>Quest items, blueprints, notes and the rest.</summary>
    Other
}

/// <summary>Names and categories of the game's item classes (the <c>Class</c> field of item records).</summary>
public static class ItemClasses
{
    /// <summary>Category of an item class; <c>null</c> for a record that is not an item (affixes, loot tables, ...).</summary>
    public static ItemCategory? GetCategory(string? cls)
    {
        if (string.IsNullOrEmpty(cls))
            return null;

        if (cls.StartsWith("Armor", StringComparison.OrdinalIgnoreCase) || cls.StartsWith("Weapon", StringComparison.OrdinalIgnoreCase))
            return ItemCategory.Gear;
        if (cls.StartsWith("OneShot", StringComparison.OrdinalIgnoreCase))
            return ItemCategory.Consumable;

        return cls switch
        {
            "ItemArtifact" => ItemCategory.Relic,
            "ItemRelic" => ItemCategory.Component,
            "ItemEnchantment" => ItemCategory.Augment,
            _ when cls.StartsWith("Item", StringComparison.OrdinalIgnoreCase)
                   || cls.StartsWith("QuestItem", StringComparison.OrdinalIgnoreCase) => ItemCategory.Other,
            _ => null
        };
    }

    /// <summary>Several of these stack in one bag cell (a stack size can be set).</summary>
    public static bool IsStackable(ItemCategory category) =>
        category is ItemCategory.Component or ItemCategory.Augment or ItemCategory.Consumable or ItemCategory.Other;

    /// <summary>"Helm", "Two-Handed Sword", "Relic"; <c>null</c> for a class without a name here.</summary>
    public static string? GetLabel(string? cls) => cls switch
    {
        null => null,
        "ArmorProtective_Head" => "Helm",
        "ArmorProtective_Chest" => "Chest Armor",
        "ArmorProtective_Shoulders" => "Shoulders",
        "ArmorProtective_Hands" => "Gloves",
        "ArmorProtective_Legs" => "Pants",
        "ArmorProtective_Feet" => "Boots",
        "ArmorProtective_Waist" => "Belt",
        "ArmorJewelry_Amulet" => "Amulet",
        "ArmorJewelry_Ring" => "Ring",
        "ArmorJewelry_Medal" => "Medal",
        "ItemArtifact" => "Relic",
        "WeaponMelee_Axe" => "One-Handed Axe",
        "WeaponMelee_Mace" => "One-Handed Mace",
        "WeaponMelee_Sword" => "One-Handed Sword",
        "WeaponMelee_Dagger" => "Dagger",
        "WeaponMelee_Scepter" => "Scepter",
        "WeaponMelee_Axe2h" => "Two-Handed Axe",
        "WeaponMelee_Mace2h" => "Two-Handed Mace",
        "WeaponMelee_Sword2h" => "Two-Handed Sword",
        "WeaponHunting_Ranged1h" => "One-Handed Ranged",
        "WeaponHunting_Ranged2h" => "Two-Handed Ranged",
        "WeaponHunting_Spear" => "Spear",
        "WeaponMagical_Staff" => "Staff",
        "WeaponArmor_Shield" => "Shield",
        "WeaponArmor_Offhand" => "Off-Hand",
        "QuestItem" => "Quest Item",
        "ItemRelic" => "Component",
        "ItemEnchantment" => "Augment",
        "ItemArtifactFormula" => "Blueprint",
        "OneShot_Scroll" => "Scroll",
        _ when cls.StartsWith("OneShot_Potion", StringComparison.OrdinalIgnoreCase) => "Potion",
        _ => null
    };

    /// <summary>Order of the groups in the item builder: armor head to toe, jewelry, weapons, then the rest.</summary>
    public static int GetGroupOrder(string? cls) => cls switch
    {
        "ArmorProtective_Head" => 0,
        "ArmorProtective_Shoulders" => 1,
        "ArmorProtective_Chest" => 2,
        "ArmorProtective_Hands" => 3,
        "ArmorProtective_Waist" => 4,
        "ArmorProtective_Legs" => 5,
        "ArmorProtective_Feet" => 6,
        "ArmorJewelry_Amulet" => 10,
        "ArmorJewelry_Ring" => 11,
        "ArmorJewelry_Medal" => 12,
        _ when cls?.StartsWith("WeaponMelee", StringComparison.OrdinalIgnoreCase) == true => 20,
        _ when cls?.StartsWith("WeaponHunting", StringComparison.OrdinalIgnoreCase) == true => 21,
        "WeaponMagical_Staff" => 22,
        "WeaponArmor_Shield" => 23,
        "WeaponArmor_Offhand" => 24,
        _ => 50
    };
}
