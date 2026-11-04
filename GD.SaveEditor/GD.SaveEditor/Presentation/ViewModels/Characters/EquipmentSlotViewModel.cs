using System.IO;
using System.Windows.Media;
using GD.SaveEditor.Services.GameData.Database;
using GD.SaveEditor.Services.GameData.Items;
using GD.SaveEditor.Services.GameData.Textures;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Presentation.ViewModels.Characters;

/// <summary>A labeled line of an item's tooltip, e.g. "Seed: 78467549".</summary>
public sealed record ItemDetailLine(string Label, string Value);

/// <summary>
/// A stat line of an item's tooltip like in the game: "+54% Elemental Damage" with its roll range "[50-61]";
/// skill bonuses ("+3 to Disintegration") are drawn in the skill color.
/// </summary>
public sealed record ItemTooltipLine(string Text, string Range = "", bool IsSkill = false)
{
    public bool HasRange => Range.Length > 0;

    /// <summary>The range with its leading space, empty without one.</summary>
    public string RangeText => HasRange ? " " + Range : string.Empty;
}

/// <summary>A part of an item added to it, shown under its stats: the component or the augment.</summary>
/// <param name="Name">"Scaled Hide".</param>
/// <param name="Kind">"Component", "Augment".</param>
public sealed record ItemTooltipSection(string Name, string Kind, ItemRarity Rarity, IReadOnlyList<ItemTooltipLine> Lines);

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

    /// <summary>"Required Player Level: 94".</summary>
    [ObservableProperty]
    private string? _requirementText;

    /// <summary>Base values of the item under its type: "1505 Armor", "31% Chance to Block 520 Damage".</summary>
    [ObservableProperty]
    private IReadOnlyList<ItemTooltipLine> _baseLines = [];

    /// <summary>Stats of the item, its affixes and crafting bonus merged like in the game, then the skill bonuses.</summary>
    [ObservableProperty]
    private IReadOnlyList<ItemTooltipLine> _statLines = [];

    /// <summary>Component and augment with their stats.</summary>
    [ObservableProperty]
    private IReadOnlyList<ItemTooltipSection> _sections = [];

    /// <summary>"Set: Aleksander's Regalia"; <c>null</c> for items without a set.</summary>
    [ObservableProperty]
    private string? _setText;

    /// <summary>"Illusion: ..."; <c>null</c> without a transmute.</summary>
    [ObservableProperty]
    private string? _illusionText;

    /// <summary>Shown when stats have roll ranges: the save only holds the seed, not the rolled values.</summary>
    [ObservableProperty]
    private string? _rollNote;

    /// <summary>
    /// Editor details under the game tooltip: record and seed, and the record names of the parts the database does
    /// not know (all of them before the database is generated).
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<ItemDetailLine> _details = [];

    /// <summary>
    /// Looks the item up in the game data and builds its tooltip like the game's. Runs on a worker thread; returns an
    /// action that applies the result on the UI thread.
    /// </summary>
    public Action Resolve(IGameItemProvider items, IGameIconProvider icons)
    {
        if (Item is not { } item)
            return () => { };

        var baseInfo = items.GetItem(item.BaseRecord);
        var prefix = items.GetItem(item.PrefixRecord);
        var suffix = items.GetItem(item.SuffixRecord);
        var modifier = items.GetItem(item.ModifierRecord);
        var component = items.GetItem(item.ComponentRecord);
        var completionBonus = items.GetItem(item.CompletionBonusRecord);
        var augment = items.GetItem(item.AugmentRecord);
        var transmute = items.GetItem(item.TransmuteRecord);

        var name = ComposeName(baseInfo, prefix, suffix) ?? FallbackName(item.BaseRecord);
        var rarity = GetDisplayRarity(baseInfo, prefix, suffix);
        var icon = icons.GetIcon(baseInfo?.BitmapPath);
        var typeText = ComposeTypeText(baseInfo, prefix, suffix, augment);

        var level = new[] { baseInfo, prefix, suffix }.Max(i => i?.LevelRequirement ?? 0);
        var requirement = level > 0 ? $"Required Player Level: {level}" : null;

        // Base values, then the merged stats of the item and its affixes (and the crafting bonus) like the game.
        var baseLines = BuildBaseLines(baseInfo);
        var stats = new ItemStatBlock();
        if (baseInfo is not null)
            stats.Add(baseInfo.Fields, baseInfo.ScalePercent, exclude: BaseLineFields);
        foreach (var affix in new[] { prefix, suffix, modifier })
        {
            if (affix is not null)
                stats.Add(affix.Fields, affix.ScalePercent);
        }

        var statLines = stats.GetLines().Select(l => new ItemTooltipLine(l.Text, l.Range ?? string.Empty)).ToList();
        statLines.AddRange(BuildSkillLines(items, baseInfo, prefix, suffix, modifier));

        var sections = new List<ItemTooltipSection>();
        if (component is not null || !string.IsNullOrEmpty(item.ComponentRecord))
        {
            var componentStats = new ItemStatBlock();
            if (component is not null)
                componentStats.Add(component.Fields, useLastValue: true);
            if (completionBonus is not null)
                componentStats.Add(completionBonus.Fields, useLastValue: true);

            var lines = ToLines(componentStats);
            lines.AddRange(BuildSkillLines(items, component, completionBonus));
            sections.Add(new ItemTooltipSection(component?.Name ?? FallbackName(item.ComponentRecord), "Component", ItemRarity.Component, lines));
        }

        if (augment is not null || !string.IsNullOrEmpty(item.AugmentRecord))
        {
            var augmentStats = new ItemStatBlock();
            if (augment is not null)
                augmentStats.Add(augment.Fields, useLastValue: true);

            var lines = ToLines(augmentStats);
            lines.AddRange(BuildSkillLines(items, augment));
            sections.Add(new ItemTooltipSection(augment?.Name ?? FallbackName(item.AugmentRecord), "Augment", ItemRarity.Rare, lines));
        }

        var setText = baseInfo?.SetName is { } setName ? $"Set: {setName}" : null;
        var illusionText = string.IsNullOrEmpty(item.TransmuteRecord)
            ? null
            : $"Illusion: {transmute?.Name ?? FallbackName(item.TransmuteRecord)}";
        var rollNote = statLines.Any(l => l.HasRange)
            ? "Base values with their roll range; the rolled values come from the seed."
            : null;

        var details = new List<ItemDetailLine>();
        AddUnresolved(details, "Prefix", prefix, item.PrefixRecord);
        AddUnresolved(details, "Suffix", suffix, item.SuffixRecord);
        // These have no names in the game: listed only when the database does not know them.
        AddUnresolved(details, "Crafting bonus", modifier, item.ModifierRecord, hasName: false);
        AddUnresolved(details, "Completion bonus", completionBonus, item.CompletionBonusRecord, hasName: false);
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
            BaseLines = baseLines;
            StatLines = statLines;
            Sections = sections;
            SetText = setText;
            IllusionText = illusionText;
            RollNote = rollNote;
            Details = details;
        };
    }

    /// <summary>Fields of the base item shown above its stats (<see cref="BuildBaseLines"/>), not in them.</summary>
    private static readonly IReadOnlySet<string> BaseLineFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "defensiveProtection", "defensiveBlock", "blockValue", "offensivePhysicalMin", "offensivePhysicalMax"
    };

    /// <summary>"1505 Armor", "31% Chance to Block 520 Damage", "120-180 Physical Damage" of the base item.</summary>
    private static List<ItemTooltipLine> BuildBaseLines(GameItemInfo? baseInfo)
    {
        var lines = new List<ItemTooltipLine>();
        if (baseInfo is null)
            return lines;

        var fields = baseInfo.Fields;
        if (ItemStatBlock.GetNumber(fields, "defensiveBlock") is { } block and > 0
            && ItemStatBlock.GetNumber(fields, "blockValue") is { } blocked and > 0)
            lines.Add(new ItemTooltipLine($"{ItemStatBlock.Number(block)}% Chance to Block {ItemStatBlock.Number(blocked)} Damage"));

        if (ItemStatBlock.GetNumber(fields, "offensivePhysicalMin") is { } min and > 0)
        {
            var max = ItemStatBlock.GetNumber(fields, "offensivePhysicalMax") is { } m && m > min ? m : min;
            lines.Add(new ItemTooltipLine(max > min
                ? $"{ItemStatBlock.Number(min)}-{ItemStatBlock.Number(max)} Physical Damage"
                : $"{ItemStatBlock.Number(min)} Physical Damage"));
        }

        if (ItemStatBlock.GetNumber(fields, "defensiveProtection") is { } armor and > 0)
            lines.Add(new ItemTooltipLine($"{ItemStatBlock.Number(armor)} Armor"));

        return lines;
    }

    /// <summary>
    /// Skill bonuses of the records: "+3 to Disintegration" (<c>augmentSkillName1..4</c>), "+1 to all skills in
    /// Necromancer" (<c>augmentMasteryName1..2</c>), "+1 to all Skills" and "Grants Skill: ..." (<c>itemSkillName</c>).
    /// </summary>
    private static IEnumerable<ItemTooltipLine> BuildSkillLines(IGameItemProvider items, params GameItemInfo?[] records)
    {
        var allSkills = 0d;
        var skills = new List<(string Record, double Level)>();
        var masteries = new List<(string Record, double Level)>();
        var granted = new List<string>();

        foreach (var record in records)
        {
            if (record is null)
                continue;

            var fields = record.Fields;
            allSkills += ItemStatBlock.GetNumber(fields, "augmentAllLevel") ?? 0;
            for (var i = 1; i <= 4; i++)
            {
                if (ItemStatBlock.GetString(fields, $"augmentSkillName{i}") is { } skill
                    && ItemStatBlock.GetNumber(fields, $"augmentSkillLevel{i}") is { } level and > 0)
                    Merge(skills, skill, level);
            }

            for (var i = 1; i <= 2; i++)
            {
                if (ItemStatBlock.GetString(fields, $"augmentMasteryName{i}") is { } mastery
                    && ItemStatBlock.GetNumber(fields, $"augmentMasteryLevel{i}") is { } level and > 0)
                    Merge(masteries, mastery, level);
            }

            if (ItemStatBlock.GetString(fields, "itemSkillName") is { } grantedSkill)
                granted.Add(grantedSkill);
        }

        if (allSkills > 0)
            yield return new ItemTooltipLine($"+{ItemStatBlock.Number(allSkills)} to all Skills", IsSkill: true);
        foreach (var (record, level) in masteries)
            yield return new ItemTooltipLine($"+{ItemStatBlock.Number(level)} to all skills in {SkillName(items, record)}", IsSkill: true);
        foreach (var (record, level) in skills)
            yield return new ItemTooltipLine($"+{ItemStatBlock.Number(level)} to {SkillName(items, record)}", IsSkill: true);
        foreach (var record in granted)
            yield return new ItemTooltipLine($"Grants Skill: {SkillName(items, record)}", IsSkill: true);

        static void Merge(List<(string Record, double Level)> list, string record, double level)
        {
            var index = list.FindIndex(s => string.Equals(s.Record, record, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                list[index] = (record, list[index].Level + level);
            else
                list.Add((record, level));
        }
    }

    private static string SkillName(IGameItemProvider items, string record) =>
        items.GetItem(record)?.Name ?? FallbackName(record);

    private static List<ItemTooltipLine> ToLines(ItemStatBlock stats) =>
        stats.GetLines().Select(l => new ItemTooltipLine(l.Text, l.Range ?? string.Empty)).ToList();

    /// <summary>The name color: affixes raise a common or magical base item ("Rare" with a rare affix).</summary>
    private static ItemRarity GetDisplayRarity(GameItemInfo? baseInfo, GameItemInfo? prefix, GameItemInfo? suffix)
    {
        var rarity = baseInfo?.Rarity ?? ItemRarity.Unknown;
        if (rarity is not (ItemRarity.Common or ItemRarity.Magical or ItemRarity.Rare))
            return rarity;

        foreach (var affix in new[] { prefix, suffix })
        {
            if (affix?.Rarity is ItemRarity.Magical or ItemRarity.Rare && affix.Rarity > rarity)
                rarity = affix.Rarity;
        }

        return rarity;
    }

    /// <summary>"Augmented Double Rare Pants", "Legendary Two-Handed Sword", "Relic".</summary>
    private string ComposeTypeText(GameItemInfo? baseInfo, GameItemInfo? prefix, GameItemInfo? suffix, GameItemInfo? augment)
    {
        var type = GetClassLabel(baseInfo?.Class) ?? Label;
        if (type == "Relic")
            return type;

        var baseRarity = baseInfo?.Rarity ?? ItemRarity.Unknown;
        var rareAffixes = new[] { prefix, suffix }.Count(a => a?.Rarity == ItemRarity.Rare);
        var magicalAffix = new[] { prefix, suffix }.Any(a => a?.Rarity == ItemRarity.Magical);

        string? rarityWord = baseRarity switch
        {
            ItemRarity.Epic or ItemRarity.Legendary => baseRarity.ToString(),
            _ when rareAffixes == 2 => "Double Rare",
            ItemRarity.Rare => "Rare",
            _ when rareAffixes == 1 => "Rare",
            ItemRarity.Magical => "Magical",
            _ when magicalAffix => "Magical",
            ItemRarity.Common => "Common",
            _ => null
        };

        var text = rarityWord is null ? type : $"{rarityWord} {type}";
        return augment is not null || !string.IsNullOrEmpty(Item?.AugmentRecord) ? $"Augmented {text}" : text;
    }

    /// <summary>Record name of a part the database does not know (or before it is generated).</summary>
    private static void AddUnresolved(List<ItemDetailLine> details, string label, GameItemInfo? info, string record, bool hasName = true)
    {
        if (!string.IsNullOrEmpty(record) && (hasName ? info?.Name is null : info is null))
            details.Add(new ItemDetailLine(label, FallbackName(record)));
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
