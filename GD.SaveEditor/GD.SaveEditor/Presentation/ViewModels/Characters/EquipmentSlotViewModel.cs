using System.IO;
using System.Windows.Media;
using GD.SaveEditor.Services.GameData.Database;
using GD.SaveEditor.Services.GameData.Textures;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Presentation.ViewModels.Characters;

/// <summary>A line of an item's tooltip, e.g. "Augment: Spellscourge Arcana".</summary>
public sealed record ItemDetailLine(string Label, string Value);

/// <summary>One slot of the equipment screen: what it holds, its name, rarity and icon.</summary>
public sealed partial class EquipmentSlotViewModel : ViewModelBase
{
    public EquipmentSlotViewModel(EquipmentSlot slot, int weaponSet, SaveItem? item)
    {
        Slot = slot;
        WeaponSet = weaponSet;
        Item = item is { IsEmpty: false } ? item : null;
        Label = GetSlotLabel(slot);
        Name = Item is null ? Label : FallbackName(Item.BaseRecord);
    }

    public EquipmentSlot Slot { get; }

    /// <summary>1 or 2 for weapon slots, 0 for the others.</summary>
    public int WeaponSet { get; }

    /// <summary>The item in the slot; <c>null</c> when the slot is empty.</summary>
    public SaveItem? Item { get; }

    public bool IsEmpty => Item is null;

    /// <summary>Name of the slot ("Head", "Main hand", ...), shown in an empty slot.</summary>
    public string Label { get; }

    /// <summary>Full item name (prefix, base name, suffix); the record's file name until the database is read.</summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>"Legendary Two-Handed Sword", "Rare Ring", ...</summary>
    [ObservableProperty]
    private string? _typeText;

    [ObservableProperty]
    private ItemRarity _rarity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon))]
    private ImageSource? _icon;

    public bool HasIcon => Icon is not null;

    [ObservableProperty]
    private string? _requirementText;

    /// <summary>Affixes, component, augment, ... for the tooltip.</summary>
    [ObservableProperty]
    private IReadOnlyList<ItemDetailLine> _details = [];

    /// <summary>
    /// Looks the item up in the game data. Runs on a worker thread; returns an action that applies the result
    /// on the UI thread.
    /// </summary>
    public Action Resolve(IGameItemProvider items, IGameIconProvider icons)
    {
        if (Item is not { } item)
            return () => { };

        var baseInfo = items.GetItem(item.BaseRecord);
        var prefix = items.GetItem(item.PrefixRecord);
        var suffix = items.GetItem(item.SuffixRecord);

        var name = ComposeName(baseInfo, prefix, suffix) ?? FallbackName(item.BaseRecord);
        var rarity = baseInfo?.Rarity ?? ItemRarity.Unknown;
        var icon = icons.GetIcon(baseInfo?.BitmapPath);

        var type = GetClassLabel(baseInfo?.Class) ?? Label;
        var rarityWord = rarity is ItemRarity.Common or ItemRarity.Magical or ItemRarity.Rare or ItemRarity.Epic or ItemRarity.Legendary
            ? rarity.ToString()
            : null;
        var typeText = rarityWord is null || type == "Relic" ? type : $"{rarityWord} {type}";
        var requirement = baseInfo?.LevelRequirement is int level and > 0 ? $"Required level {level}" : null;

        var details = new List<ItemDetailLine>();
        AddDetail(details, "Prefix", prefix, item.PrefixRecord);
        AddDetail(details, "Suffix", suffix, item.SuffixRecord);
        AddDetail(details, "Component", items.GetItem(item.ComponentRecord), item.ComponentRecord);
        AddDetail(details, "Completion bonus", items.GetItem(item.CompletionBonusRecord), item.CompletionBonusRecord, useRecordNameOnly: true);
        AddDetail(details, "Augment", items.GetItem(item.AugmentRecord), item.AugmentRecord);
        AddDetail(details, "Crafting bonus", items.GetItem(item.ModifierRecord), item.ModifierRecord, useRecordNameOnly: true);
        AddDetail(details, "Illusion", items.GetItem(item.TransmuteRecord), item.TransmuteRecord);
        if (baseInfo?.SetName is { } setName)
            details.Add(new ItemDetailLine("Set", setName));
        if (item.StackCount > 1)
            details.Add(new ItemDetailLine("Stack", item.StackCount.ToString()));
        details.Add(new ItemDetailLine("Record", item.BaseRecord));
        details.Add(new ItemDetailLine("Seed", item.Seed.ToString()));

        return () =>
        {
            Name = name;
            Rarity = rarity;
            Icon = icon;
            TypeText = typeText;
            RequirementText = requirement;
            Details = details;
        };
    }

    /// <summary>The game's naming: prefix, style, quality, base name, suffix.</summary>
    private static string? ComposeName(GameItemInfo? baseInfo, GameItemInfo? prefix, GameItemInfo? suffix)
    {
        if (baseInfo?.Name is not { } baseName)
            return null;

        var parts = new List<string>(5);
        if (prefix?.Name is { } prefixName) parts.Add(prefixName);
        if (baseInfo.StyleText is { } style) parts.Add(style);
        if (baseInfo.QualityText is { } quality && !baseName.StartsWith(quality, StringComparison.OrdinalIgnoreCase)) parts.Add(quality);
        parts.Add(baseName);
        if (suffix?.Name is { } suffixName) parts.Add(suffixName);
        return string.Join(' ', parts);
    }

    private static void AddDetail(List<ItemDetailLine> details, string label, GameItemInfo? info, string record, bool useRecordNameOnly = false)
    {
        if (string.IsNullOrEmpty(record))
            return;

        var value = !useRecordNameOnly && info?.Name is { } name ? name : FallbackName(record);
        details.Add(new ItemDetailLine(label, value));
    }

    /// <summary>"records/items/gearhead/a01_head001.dbr" → "a01_head001".</summary>
    private static string FallbackName(string record) => Path.GetFileNameWithoutExtension(record.Replace('\\', '/'));

    private static string GetSlotLabel(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.Ring1 or EquipmentSlot.Ring2 => "Ring",
        EquipmentSlot.Waist => "Belt",
        EquipmentSlot.MainHand => "Main hand",
        EquipmentSlot.OffHand => "Off hand",
        _ => slot.ToString()
    };

    private static string? GetClassLabel(string? cls) => cls switch
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
        _ => null
    };
}
