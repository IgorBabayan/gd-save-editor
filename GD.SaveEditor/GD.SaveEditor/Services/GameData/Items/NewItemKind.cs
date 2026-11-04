namespace GD.SaveEditor.Services.GameData.Items;

/// <summary>
/// What kind of item to build, picked from the "Build a new item" menu of a bag: the item builder then lists only
/// items of that kind (like a slot of the Last Epoch editor).
/// </summary>
public enum NewItemKind
{
    Helm,
    Shoulders,
    ChestArmor,
    Gloves,
    Belt,
    Pants,
    Boots,
    Weapon,
    OffHand,
    Amulet,
    Ring,
    Medal,
    Relic,
    Component,
    Augment,
    Consumable,
    Other
}

/// <summary>Names, menu groups, icons and item matching of <see cref="NewItemKind"/>.</summary>
public static class NewItemKinds
{
    /// <summary>The entries of the "Build a new item" menu, a separator between groups: armor, weapons, jewelry, the rest.</summary>
    public static IReadOnlyList<IReadOnlyList<NewItemKind>> MenuGroups { get; } =
    [
        [NewItemKind.Helm, NewItemKind.Shoulders, NewItemKind.ChestArmor, NewItemKind.Gloves, NewItemKind.Belt, NewItemKind.Pants, NewItemKind.Boots],
        [NewItemKind.Weapon, NewItemKind.OffHand],
        [NewItemKind.Amulet, NewItemKind.Ring, NewItemKind.Medal, NewItemKind.Relic],
        [NewItemKind.Component, NewItemKind.Augment, NewItemKind.Consumable, NewItemKind.Other]
    ];

    /// <summary>"Helm", "Chest armor", "Off-hand", ...</summary>
    public static string GetName(NewItemKind kind) => kind switch
    {
        NewItemKind.ChestArmor => "Chest armor",
        NewItemKind.OffHand => "Off-hand",
        NewItemKind.Other => "Other item",
        _ => kind.ToString()
    };

    /// <summary>Name of a <c>Material.Icons</c> icon for the menu entry (an unknown name shows no icon).</summary>
    public static string GetIconName(NewItemKind kind) => kind switch
    {
        NewItemKind.Helm => "HardHat",
        NewItemKind.Shoulders => "TshirtVOutline",
        NewItemKind.ChestArmor => "TshirtCrew",
        NewItemKind.Gloves => "HandBackLeft",
        NewItemKind.Belt => "Seatbelt",
        NewItemKind.Pants => "Hanger",
        NewItemKind.Boots => "ShoeFormal",
        NewItemKind.Weapon => "Sword",
        NewItemKind.OffHand => "Shield",
        NewItemKind.Amulet => "Necklace",
        NewItemKind.Ring => "Ring",
        NewItemKind.Medal => "Medal",
        NewItemKind.Relic => "TreasureChest",
        NewItemKind.Component => "Puzzle",
        NewItemKind.Augment => "FlaskOutline",
        NewItemKind.Consumable => "BottleTonicPlus",
        _ => "DotsHorizontal"
    };

    /// <summary>The builder's category of the kind (it selects that category's pill).</summary>
    public static ItemCategory GetCategory(NewItemKind kind) => kind switch
    {
        NewItemKind.Relic => ItemCategory.Relic,
        NewItemKind.Component => ItemCategory.Component,
        NewItemKind.Augment => ItemCategory.Augment,
        NewItemKind.Consumable => ItemCategory.Consumable,
        NewItemKind.Other => ItemCategory.Other,
        _ => ItemCategory.Gear
    };

    /// <summary><paramref name="item"/> is of <paramref name="kind"/>.</summary>
    public static bool Matches(NewItemKind kind, CatalogItem item)
    {
        var cls = item.Class ?? string.Empty;
        return kind switch
        {
            NewItemKind.Helm => cls == "ArmorProtective_Head",
            NewItemKind.Shoulders => cls == "ArmorProtective_Shoulders",
            NewItemKind.ChestArmor => cls == "ArmorProtective_Chest",
            NewItemKind.Gloves => cls == "ArmorProtective_Hands",
            NewItemKind.Belt => cls == "ArmorProtective_Waist",
            NewItemKind.Pants => cls == "ArmorProtective_Legs",
            NewItemKind.Boots => cls == "ArmorProtective_Feet",
            NewItemKind.Weapon => cls.StartsWith("WeaponMelee", StringComparison.OrdinalIgnoreCase)
                                  || cls.StartsWith("WeaponHunting", StringComparison.OrdinalIgnoreCase)
                                  || cls.StartsWith("WeaponMagical", StringComparison.OrdinalIgnoreCase),
            NewItemKind.OffHand => cls is "WeaponArmor_Shield" or "WeaponArmor_Offhand",
            NewItemKind.Amulet => cls == "ArmorJewelry_Amulet",
            NewItemKind.Ring => cls == "ArmorJewelry_Ring",
            NewItemKind.Medal => cls == "ArmorJewelry_Medal",
            _ => item.Category == GetCategory(kind)
        };
    }
}
