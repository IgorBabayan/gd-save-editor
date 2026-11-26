using Avalonia.Media.Imaging;
using Avalonia.Threading;
using GD.SaveEditor.Services.GameData.Database;
using GD.SaveEditor.Services.GameData.Items;
using GD.SaveEditor.Services.GameData.Textures;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Presentation.ViewModels.Inventory;

/// <summary>An item made in the item builder, with the cells it covers in a bag (from its icon).</summary>
public sealed record BuiltItem(SaveItem Item, int Columns, int Rows);

/// <summary>A pill of the item builder ("Gear", "Rare", ...); <see cref="Value"/> is <c>null</c> for "All" / "Any".</summary>
public sealed partial class FilterOptionViewModel(string name, object? value) : ViewModelBase
{
    public string Name { get; } = name;

    public object? Value { get; } = value;

    /// <summary>Rarity pills show a dot in the rarity's color; category pills have none.</summary>
    public bool HasDot => Value is ItemRarity;

    public ItemRarity DotRarity => Value as ItemRarity? ?? ItemRarity.Unknown;

    /// <summary>Selected only by <see cref="ItemBuilderViewModel"/>, so a declined change never moves the pills.</summary>
    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// An item of the builder's list. Its icon is read from the game files the first time the row is shown, on a worker
/// thread, so a long list opens at once.
/// </summary>
public sealed partial class ItemOptionViewModel(CatalogItem item, IGameIconProvider icons) : ViewModelBase
{
    private bool _iconRequested;

    public CatalogItem Item { get; } = item;

    public string Name => Item.Name;

    /// <summary>Group header of the list: the item type ("Helm", "Component", ...).</summary>
    public string GroupName => Item.TypeLabel;

    /// <summary>Name, type, set and record path in upper case, built once: the search scans it for each typed word.</summary>
    internal string SearchKey { get; } = string.Join('\n', item.Name, item.TypeLabel, item.SetName ?? string.Empty, item.RecordPath)
        .ToUpperInvariant();

    public ItemRarity Rarity => Item.Rarity;

    /// <summary>"Epic Helm · Level 94 · Set: Aleksander's Regalia".</summary>
    public string Details
    {
        get
        {
            var parts = new List<string>(3);
            parts.Add(Item.Category == ItemCategory.Gear ? $"{Item.Rarity} {Item.TypeLabel}" : Item.TypeLabel);
            if (Item.LevelRequirement > 0)
                parts.Add($"Level {Item.LevelRequirement}");
            if (Item.SetName is { } set)
                parts.Add($"Set: {set}");
            return string.Join(" · ", parts);
        }
    }

    /// <summary>Record path, shown as the row's tooltip (records with the same name differ by it).</summary>
    public string RecordPath => Item.RecordPath;

    /// <summary>The icon; reading it starts when the row first asks for it.</summary>
    public Bitmap? Icon
    {
        get
        {
            if (!_iconRequested)
            {
                _iconRequested = true;
                _ = LoadIconAsync();
            }

            return _icon;
        }
    }

    private Bitmap? _icon;

    /// <summary>Reads the icon now (the builder needs it to tell the item's size); <c>null</c> when it cannot be read.</summary>
    public Bitmap? GetIconNow()
    {
        _icon ??= SafeGetIcon();
        _iconRequested = true;
        return _icon;
    }

    /// <summary>Cells the item covers in a bag: the game draws item icons at 32 pixels per cell.</summary>
    public (int Columns, int Rows)? GetSize() => GetIconNow() is { } bitmap
        ? (Math.Clamp((int)Math.Round(bitmap.PixelSize.Width / InventoryItemViewModel.CellSize), 1, 4),
           Math.Clamp((int)Math.Round(bitmap.PixelSize.Height / InventoryItemViewModel.CellSize), 1, 4))
        : null;

    private async Task LoadIconAsync()
    {
        var icon = await Task.Run(SafeGetIcon);
        _icon ??= icon;
        if (!Dispatcher.UIThread.CheckAccess())
            Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(Icon)));
        else
            OnPropertyChanged(nameof(Icon));
    }

    private Bitmap? SafeGetIcon()
    {
        try
        {
            return icons.GetIcon(Item.BitmapPath);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException
                                       or System.IO.InvalidDataException or NotSupportedException)
        {
            return null;
        }
    }
}

/// <summary>An affix, bonus, component or augment in the builder's picker.</summary>
public sealed class PartOptionViewModel(CatalogPart part, string groupName)
{
    public CatalogPart Part { get; } = part;

    public string Name => Part.Name;

    /// <summary>Group header of the picker ("Rare affixes", "Rolls on this relic", ...).</summary>
    public string GroupName { get; } = groupName;

    /// <summary>Name, summary, code and group in upper case, built once: the search scans it for each typed word.</summary>
    internal string SearchKey { get; } = string.Join('\n', part.Name, part.Summary, part.Code, groupName).ToUpperInvariant();

    public ItemRarity Rarity => Part.Rarity;

    /// <summary>"Level 58 · b_ar019_je_f".</summary>
    public string Details => Part.LevelRequirement > 0 ? $"Level {Part.LevelRequirement} · {Part.Code}" : Part.Code;

    /// <summary>Stats of the record on one line.</summary>
    public string Summary => Part.Summary;

    public bool HasSummary => Summary.Length > 0 && !string.Equals(Summary, Name, StringComparison.Ordinal);
}

/// <summary>
/// A part of the built item in the builder: prefix, suffix, crafting bonus, component, completion bonus or augment.
/// Empty, it offers "Add …"; filled, its name and stats with "Change" and "Remove".
/// </summary>
public sealed partial class ItemPartSlotViewModel : ViewModelBase
{
    private readonly Action<ItemPartSlotViewModel> _choose;
    private readonly Action<ItemPartSlotViewModel> _remove;

    public ItemPartSlotViewModel(ItemPartKind kind, Action<ItemPartSlotViewModel> choose, Action<ItemPartSlotViewModel> remove)
    {
        Kind = kind;
        _choose = choose;
        _remove = remove;
    }

    public ItemPartKind Kind { get; }

    /// <summary>"Prefix", "Suffix", "Crafting bonus", "Component", "Completion bonus", "Augment".</summary>
    public string Label => Kind switch
    {
        ItemPartKind.Prefix => "Prefix",
        ItemPartKind.Suffix => "Suffix",
        ItemPartKind.CraftingBonus => "Crafting bonus",
        ItemPartKind.CompletionBonus => "Completion bonus",
        ItemPartKind.Component => "Component",
        _ => "Augment"
    };

    public string AddText => $"Add {Label.ToLowerInvariant()}";

    /// <summary>The part; <c>null</c> while the slot is empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPart), nameof(Name), nameof(Summary), nameof(HasSummary), nameof(Rarity))]
    private CatalogPart? _part;

    public bool HasPart => Part is not null;

    public string Name => Part?.Name ?? string.Empty;

    public string Summary => Part?.Summary ?? string.Empty;

    public bool HasSummary => Summary.Length > 0 && !string.Equals(Summary, Name, StringComparison.Ordinal);

    public ItemRarity Rarity => Part?.Rarity ?? ItemRarity.Unknown;

    /// <summary>The selected item can take this part (prefixes only on gear, completion bonuses on relics, ...).</summary>
    [ObservableProperty]
    private bool _isAvailable;

    /// <summary>The picker is open for this slot.</summary>
    [ObservableProperty]
    private bool _isPicking;

    [RelayCommand]
    private void Choose() => _choose(this);

    [RelayCommand]
    private void Remove() => _remove(this);
}

/// <summary>Header row of a grouped list ("Helm", "Rare prefixes", ...): shown above its items, never selected.</summary>
public sealed record ListGroupHeader(string Name)
{
    /// <summary>
    /// Rows of <paramref name="items"/> grouped by <paramref name="groupOf"/>: each group's header, then its items. Groups
    /// keep the order in which they first appear, items their order inside a group (like a grouped WPF collection view).
    /// </summary>
    public static IReadOnlyList<object> Group<T>(IReadOnlyList<T> items, Func<T, string> groupOf) where T : notnull
    {
        if (items.Count == 0)
            return [];

        var groups = new Dictionary<string, List<T>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var item in items)
        {
            var name = groupOf(item);
            if (!groups.TryGetValue(name, out var members))
            {
                groups[name] = members = [];
                order.Add(name);
            }

            members.Add(item);
        }

        var rows = new List<object>(items.Count + order.Count);
        foreach (var name in order)
        {
            rows.Add(new ListGroupHeader(name));
            foreach (var item in groups[name])
                rows.Add(item);
        }

        return rows;
    }
}
