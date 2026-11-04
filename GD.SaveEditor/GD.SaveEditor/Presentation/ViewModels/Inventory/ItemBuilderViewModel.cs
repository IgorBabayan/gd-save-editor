using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using GD.SaveEditor.Presentation.ViewModels.Characters;
using GD.SaveEditor.Services.GameData.Database;
using GD.SaveEditor.Services.GameData.Items;
using GD.SaveEditor.Services.GameData.Textures;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Presentation.ViewModels.Inventory;

/// <summary>
/// Item builder of a bag item, laid out like the Last Epoch editor's: category and rarity pills over a searchable list of
/// every item of the game database (grouped by type), a preview card with the game's tooltip of the item as built, and
/// a box per part the selected item can take. Grim Dawn stores an item as record paths plus a seed, so the parts are
/// records picked from lists:
/// <list type="bullet">
/// <item>gear: prefix, suffix, crafting (blacksmith) bonus, component and augment;</item>
/// <item>relics: completion bonus (the ones the relic rolls first) and crafting bonus;</item>
/// <item>a component with a bonus table: its completion bonus.</item>
/// </list>
/// The rolled values come from the seed (shown, editable and rerollable); stackable items take a stack size. Editing
/// keeps what the builder does not show (illusion, the game's extra fields).
/// </summary>
public sealed partial class ItemBuilderViewModel : ViewModelBase
{
    /// <summary>Largest stack the builder makes.</summary>
    public const uint MaxQuantity = 9_999;

    /// <summary>Rows the item list shows at most; a search narrows it.</summary>
    private const int MaxListedItems = 400;

    /// <summary>Entries the picker shows at most; a search narrows it.</summary>
    private const int MaxListedParts = 400;

    private readonly IItemCatalog _catalog;
    private readonly IGameItemProvider _items;
    private readonly IGameIconProvider _icons;
    private readonly Func<string, string, bool> _confirm;

    /// <summary>The item being edited; <c>null</c> for a new one.</summary>
    private readonly SaveItem? _current;

    private IReadOnlyList<ItemOptionViewModel> _allOptions = [];
    private FilterOptionViewModel _selectedCategory;
    private FilterOptionViewModel _selectedRarity;

    /// <summary>Completion bonuses the selected item rolls (its bonus table); empty when it has none.</summary>
    private IReadOnlyList<CatalogPart> _rolledBonuses = [];

    /// <summary>Counts preview requests, so an older preview finishing late does not replace a newer one.</summary>
    private int _previewVersion;

    public ItemBuilderViewModel(
        IItemCatalog catalog,
        IGameItemProvider items,
        IGameIconProvider icons,
        Func<string, string, bool> confirm,
        SaveItem? current)
    {
        _catalog = catalog;
        _items = items;
        _icons = icons;
        _confirm = confirm;
        _current = current;
        CanRemove = current is not null;
        IsDatabaseAvailable = catalog.IsAvailable;
        // The list is read once the window shows (LoadAsync); until then it says so.
        _isLoading = IsDatabaseAvailable;

        Categories =
        [
            new("All", null),
            new("Gear", ItemCategory.Gear),
            new("Relics", ItemCategory.Relic),
            new("Components", ItemCategory.Component),
            new("Augments", ItemCategory.Augment),
            new("Consumables", ItemCategory.Consumable),
            new("Other", ItemCategory.Other)
        ];
        Rarities =
        [
            new("Any", null),
            new("Common", ItemRarity.Common),
            new("Magical", ItemRarity.Magical),
            new("Rare", ItemRarity.Rare),
            new("Epic", ItemRarity.Epic),
            new("Legendary", ItemRarity.Legendary)
        ];
        _selectedCategory = Categories[0];
        _selectedCategory.IsSelected = true;
        _selectedRarity = Rarities[0];
        _selectedRarity.IsSelected = true;

        PartSlots =
        [
            new(ItemPartKind.Prefix, OpenPicker, RemovePart),
            new(ItemPartKind.Suffix, OpenPicker, RemovePart),
            new(ItemPartKind.CompletionBonus, OpenPicker, RemovePart),
            new(ItemPartKind.CraftingBonus, OpenPicker, RemovePart),
            new(ItemPartKind.Component, OpenPicker, RemovePart),
            new(ItemPartKind.Augment, OpenPicker, RemovePart)
        ];

        _seedText = (current?.Seed ?? NewSeed()).ToString(CultureInfo.InvariantCulture);
        _quantityText = Math.Max(1, current?.Quantity ?? 1).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Raised when the dialog should close; the argument is the built item, or <c>null</c> when cancelled.</summary>
    public event EventHandler<BuiltItem?>? CloseRequested;

    /// <summary>Raised when the user confirmed removing the item being edited; the dialog closes right after.</summary>
    public event EventHandler? RemoveRequested;

    public string Title => _current is null ? "Item builder — new item" : "Item builder — edit item";

    /// <summary><c>false</c> when <c>database.db</c> is missing: the user has to generate it first.</summary>
    public bool IsDatabaseAvailable { get; }

    /// <summary>The item list is being read from the game database.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyListText))]
    private bool _isLoading;

    /// <summary>The catalog was (or is being) read: the inspector and the full window share one view model.</summary>
    private bool _loadStarted;

    /// <summary>
    /// Reads the catalog on a worker thread, then selects the edited item and puts its parts back. Only the first call
    /// does anything (the inspector and the full builder window both call it when they show).
    /// </summary>
    public async Task LoadAsync()
    {
        if (_loadStarted)
            return;
        _loadStarted = true;

        if (!IsDatabaseAvailable)
        {
            OnPropertyChanged(nameof(EmptyListText));
            return;
        }

        IsLoading = true;
        try
        {
            var current = _current;
            var (catalogItems, currentParts) = await Task.Run(() =>
            {
                var list = _catalog.GetItems();
                List<(ItemPartKind Kind, CatalogPart Part)> parts = current is null ? [] : ReadParts(current);
                return (list, parts);
            });

            _allOptions = catalogItems.Select(i => new ItemOptionViewModel(i, _icons)).ToList();

            if (current is not null)
            {
                var match = _allOptions.FirstOrDefault(o => string.Equals(o.Item.RecordPath, current.BaseRecord.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    SelectFilters(match.Item);
                    _restoring = true;
                    SelectedOption = match;
                    _restoring = false;
                    foreach (var (kind, part) in currentParts)
                    {
                        if (PartSlots.FirstOrDefault(s => s.Kind == kind) is { } slot)
                            slot.Part = part;
                    }
                }
                else
                {
                    UnknownBaseText = $"The item ({System.IO.Path.GetFileNameWithoutExtension(current.BaseRecord)}) is not in the game database's item list. Pick another item, or remove it.";
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
        {
            UnknownBaseText = $"The game database could not be read: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }

        ApplyFilter();
        UpdatePartAvailability();
        RefreshPreview();
    }

    /// <summary>Shown when the edited item is not in the item list (or the database could not be read).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnknownBaseText), nameof(HeaderName))]
    private string? _unknownBaseText;

    public bool HasUnknownBaseText => UnknownBaseText is not null;

    /// <summary>The parts of the edited item as catalog entries (one the catalog does not list is named by its record).</summary>
    private List<(ItemPartKind Kind, CatalogPart Part)> ReadParts(SaveItem item)
    {
        var parts = new List<(ItemPartKind, CatalogPart)>();
        Add(ItemPartKind.Prefix, item.PrefixRecord);
        Add(ItemPartKind.Suffix, item.SuffixRecord);
        Add(ItemPartKind.CraftingBonus, item.ModifierRecord);
        Add(ItemPartKind.Component, item.ComponentRecord);
        Add(ItemPartKind.CompletionBonus, item.CompletionBonusRecord);
        Add(ItemPartKind.Augment, item.AugmentRecord);
        return parts;

        void Add(ItemPartKind kind, string record)
        {
            if (string.IsNullOrWhiteSpace(record))
                return;

            var path = record.Replace('\\', '/');
            var part = _catalog.GetParts(kind).FirstOrDefault(p => string.Equals(p.RecordPath, path, StringComparison.OrdinalIgnoreCase))
                       ?? new CatalogPart(path, kind, _items.GetItem(path)?.Name ?? System.IO.Path.GetFileNameWithoutExtension(path),
                           ItemRarity.Unknown, 0, string.Empty, System.IO.Path.GetFileNameWithoutExtension(path));
            parts.Add((kind, part));
        }
    }

    #region Filters and item list

    public IReadOnlyList<FilterOptionViewModel> Categories { get; }

    public IReadOnlyList<FilterOptionViewModel> Rarities { get; }

    /// <summary>Rarity pills apply to gear only (the other categories have one rarity each).</summary>
    public bool HasRarityFilter => _selectedCategory.Value is null or ItemCategory.Gear;

    [RelayCommand]
    private void SelectCategory(FilterOptionViewModel? option)
    {
        if (option is null || option == _selectedCategory)
            return;

        Select(ref _selectedCategory, option);
        OnPropertyChanged(nameof(HasRarityFilter));
        OnPropertyChanged(nameof(SelectedCategoryName));
        ApplyFilter();
    }

    [RelayCommand]
    private void SelectRarity(FilterOptionViewModel? option)
    {
        if (option is null || option == _selectedRarity)
            return;

        Select(ref _selectedRarity, option);
        OnPropertyChanged(nameof(SelectedRarity));
        ApplyFilter();
    }

    private static void Select(ref FilterOptionViewModel selected, FilterOptionViewModel option)
    {
        selected.IsSelected = false;
        selected = option;
        selected.IsSelected = true;
    }

    /// <summary>Starts the list on the edited item's category, so it is listed among its kind.</summary>
    private void SelectFilters(CatalogItem item)
    {
        if (Categories.FirstOrDefault(c => c.Value is ItemCategory category && category == item.Category) is { } option)
            Select(ref _selectedCategory, option);
        OnPropertyChanged(nameof(HasRarityFilter));
        OnPropertyChanged(nameof(SelectedCategoryName));
    }

    /// <summary>Items matching the pills and <see cref="SearchText"/>, grouped by type (at most <see cref="MaxListedItems"/>).</summary>
    public ObservableCollection<ItemOptionViewModel> Options { get; } = [];

    public bool HasOptions => Options.Count > 0;

    /// <summary>Words to find in the name, type, set or record of an item.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    /// <summary>"Showing 400 of 2,315 items. Type more to narrow the list."; empty when everything is listed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasListLimitText))]
    private string _listLimitText = string.Empty;

    public bool HasListLimitText => ListLimitText.Length > 0;

    public string EmptyListText =>
        !IsDatabaseAvailable ? "The game database was not found. Generate it with the database button in the title bar, then open the builder again."
        : IsLoading ? "Reading the items of the game database…"
        : _allOptions.Count == 0 ? "The game database has no items. Update it with the database button in the title bar."
        : "No items match the filters.";

    private void ApplyFilter()
    {
        var selected = SelectedOption;
        var words = SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var category = _selectedCategory.Value as ItemCategory?;
        var rarity = HasRarityFilter ? _selectedRarity.Value as ItemRarity? : null;

        var matches = _allOptions.Where(o =>
                (category is null || o.Item.Category == category)
                && (rarity is null || o.Item.Rarity == rarity)
                && words.All(w => o.Name.Contains(w, StringComparison.CurrentCultureIgnoreCase)
                                  || o.GroupName.Contains(w, StringComparison.CurrentCultureIgnoreCase)
                                  || (o.Item.SetName?.Contains(w, StringComparison.CurrentCultureIgnoreCase) ?? false)
                                  || o.Item.RecordPath.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Options.Clear();
        foreach (var option in matches.Take(MaxListedItems))
            Options.Add(option);

        // The selection stays listed (the ListBox would clear it otherwise).
        if (selected is not null && !Options.Contains(selected))
            Options.Insert(0, selected);

        ListLimitText = matches.Count > MaxListedItems
            ? $"Showing {MaxListedItems:N0} of {matches.Count:N0} items. Type in the search box to narrow the list."
            : string.Empty;
        OnPropertyChanged(nameof(HasOptions));
        OnPropertyChanged(nameof(EmptyListText));
    }

    #endregion

    #region Selected item

    /// <summary>Set while the edited item is selected again, so its parts are not cleared on the way.</summary>
    private bool _restoring;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(IsStackable), nameof(SizeError), nameof(HasSizeError), nameof(QuantityError), nameof(HasQuantityError))]
    [NotifyPropertyChangedFor(nameof(HeaderName), nameof(HeaderType), nameof(HeaderIcon), nameof(HeaderRarity), nameof(SelectedItemText))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private ItemOptionViewModel? _selectedOption;

    public bool HasSelection => SelectedOption is not null;

    /// <summary>The selected item stacks: a stack size can be set.</summary>
    public bool IsStackable => SelectedOption?.Item.IsStackable == true;

    partial void OnSelectedOptionChanged(ItemOptionViewModel? value)
    {
        if (value is not null)
            UnknownBaseText = null;
        OnPropertyChanged(nameof(HeaderName));

        _rolledBonuses = value is null ? [] : _catalog.GetCompletionBonuses(value.Item.RecordPath);
        // Another item drops the parts it cannot take; the edited item keeps all of its own.
        UpdatePartAvailability(keepFilled: _restoring);
        if (!_restoring)
            RemoveUnavailableParts();
        if (!IsStackable)
            QuantityText = "1";

        RefreshPreview();
    }

    /// <summary>
    /// Which parts the selected item takes. With <paramref name="keepFilled"/>, a filled completion bonus stays (an edited
    /// item may carry one this editor would not offer, e.g. of a component without a bonus table in the database).
    /// </summary>
    private void UpdatePartAvailability(bool keepFilled = true)
    {
        var item = SelectedOption?.Item;
        foreach (var slot in PartSlots)
        {
            slot.IsAvailable = item is not null && slot.Kind switch
            {
                ItemPartKind.CompletionBonus when keepFilled && slot.HasPart => item.Category is ItemCategory.Gear or ItemCategory.Relic or ItemCategory.Component,
                ItemPartKind.Prefix or ItemPartKind.Suffix or ItemPartKind.Component or ItemPartKind.Augment => item.Category == ItemCategory.Gear,
                ItemPartKind.CraftingBonus => item.Category is ItemCategory.Gear or ItemCategory.Relic,
                // A relic rolls one; a component only when it has a bonus table. A component slotted into gear brings its own.
                ItemPartKind.CompletionBonus => item.Category == ItemCategory.Relic || _rolledBonuses.Count > 0
                                                || (item.Category == ItemCategory.Gear && ComponentSlot.Part is { } component
                                                    && _catalog.GetCompletionBonuses(component.RecordPath).Count > 0),
                _ => false
            };
        }

        OnPropertyChanged(nameof(HasParts));
        OnPropertyChanged(nameof(PartsHint));
        OnPropertyChanged(nameof(PartSummary));
    }

    /// <summary>Parts the new selection cannot take are dropped (after the list changed the item kind).</summary>
    private void RemoveUnavailableParts()
    {
        foreach (var slot in PartSlots.Where(s => !s.IsAvailable && s.HasPart))
            slot.Part = null;
        if (PickerSlot is { IsAvailable: false })
            PickerSlot = null;
    }

    /// <summary>Cells the selected item covers, from its icon; <c>null</c> while it cannot be told.</summary>
    private (int Columns, int Rows)? SelectedSize => SelectedOption?.GetSize();

    /// <summary>Why the selection cannot be applied: its icon (which gives its size in the bag) could not be read.</summary>
    public string? SizeError => SelectedOption is not null && SelectedSize is null
        ? "The icon of this item could not be read from the game files, so its size in the bag is not known. Check the game folder in the settings."
        : null;

    public bool HasSizeError => SizeError is not null;

    #endregion

    #region Parts

    /// <summary>Every part slot; the view shows those <see cref="ItemPartSlotViewModel.IsAvailable"/> for the selection.</summary>
    public IReadOnlyList<ItemPartSlotViewModel> PartSlots { get; }

    private ItemPartSlotViewModel ComponentSlot => PartSlots.First(s => s.Kind == ItemPartKind.Component);

    private ItemPartSlotViewModel CompletionSlot => PartSlots.First(s => s.Kind == ItemPartKind.CompletionBonus);

    public bool HasParts => PartSlots.Any(s => s.IsAvailable);

    /// <summary>A note under the parts: what the game does with them.</summary>
    public string PartsHint => SelectedOption?.Item switch
    {
        { Category: ItemCategory.Gear, Rarity: ItemRarity.Epic or ItemRarity.Legendary } =>
            "The game does not roll a prefix or suffix on epic and legendary items; added ones are still kept and shown by the game.",
        { Category: ItemCategory.Gear } => "Prefixes and suffixes are not limited to the ones this item type rolls in the game.",
        { Category: ItemCategory.Relic } when _rolledBonuses.Count == 0 => "The bonus table of this relic was not found; all completion bonuses are offered.",
        _ => string.Empty
    };

    /// <summary>Slot the picker fills; <c>null</c> while the item list is shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPicking), nameof(PickerTitle))]
    private ItemPartSlotViewModel? _pickerSlot;

    public bool IsPicking => PickerSlot is not null;

    public string PickerTitle => PickerSlot is { } slot ? $"Choose {slot.Label.ToLowerInvariant()}" : string.Empty;

    /// <summary>Entries for <see cref="PickerSlot"/> matching <see cref="PickerSearchText"/>.</summary>
    public ObservableCollection<PartOptionViewModel> PickerOptions { get; } = [];

    public bool HasPickerOptions => PickerOptions.Count > 0;

    [ObservableProperty]
    private string _pickerSearchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPickerLimitText))]
    private string _pickerLimitText = string.Empty;

    public bool HasPickerLimitText => PickerLimitText.Length > 0;

    /// <summary>Picking an entry puts it into <see cref="PickerSlot"/> and closes the picker.</summary>
    [ObservableProperty]
    private PartOptionViewModel? _selectedPickerOption;

    partial void OnPickerSearchTextChanged(string value) => LoadPickerOptions();

    partial void OnPickerSlotChanged(ItemPartSlotViewModel? value)
    {
        foreach (var slot in PartSlots)
            slot.IsPicking = slot == value;

        PickerSearchText = string.Empty;
        LoadPickerOptions();
    }

    partial void OnSelectedPickerOptionChanged(PartOptionViewModel? value)
    {
        if (value is null || PickerSlot is not { } slot)
            return;

        slot.Part = value.Part;
        PickerSlot = null;
        SelectedPickerOption = null;
        OnPartsChanged(slot);
    }

    [RelayCommand]
    private void ClosePicker() => PickerSlot = null;

    private void OpenPicker(ItemPartSlotViewModel slot) => PickerSlot = slot;

    private void RemovePart(ItemPartSlotViewModel slot)
    {
        slot.Part = null;
        if (PickerSlot == slot)
            PickerSlot = null;
        OnPartsChanged(slot);
    }

    private void OnPartsChanged(ItemPartSlotViewModel changed)
    {
        // A component's completion bonus belongs to that component.
        if (changed.Kind == ItemPartKind.Component && SelectedOption?.Item.Category == ItemCategory.Gear)
        {
            CompletionSlot.Part = null;
            UpdatePartAvailability();
        }

        OnPropertyChanged(nameof(PartSummary));
        RefreshPreview();
    }

    private void LoadPickerOptions()
    {
        PickerOptions.Clear();
        PickerLimitText = string.Empty;
        if (PickerSlot is not { } slot || SelectedOption is not { } selected)
        {
            OnPropertyChanged(nameof(HasPickerOptions));
            return;
        }

        var options = GetPickerParts(slot.Kind, selected.Item);
        var words = PickerSearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matches = options
            .Where(o => words.All(w => o.Name.Contains(w, StringComparison.CurrentCultureIgnoreCase)
                                       || o.Summary.Contains(w, StringComparison.CurrentCultureIgnoreCase)
                                       || o.Part.Code.Contains(w, StringComparison.OrdinalIgnoreCase)
                                       || o.GroupName.Contains(w, StringComparison.CurrentCultureIgnoreCase)))
            .ToList();

        foreach (var option in matches.Take(MaxListedParts))
            PickerOptions.Add(option);

        if (matches.Count > MaxListedParts)
            PickerLimitText = $"Showing {MaxListedParts:N0} of {matches.Count:N0}. Type in the search box to narrow the list.";
        OnPropertyChanged(nameof(HasPickerOptions));
    }

    /// <summary>
    /// Entries for a part, grouped: affixes by rarity; completion bonuses the item (or its component) rolls first, then
    /// all others; components and augments by name.
    /// </summary>
    private List<PartOptionViewModel> GetPickerParts(ItemPartKind kind, CatalogItem item)
    {
        if (kind == ItemPartKind.CompletionBonus)
        {
            var rolled = item.Category == ItemCategory.Gear && ComponentSlot.Part is { } component
                ? _catalog.GetCompletionBonuses(component.RecordPath)
                : _rolledBonuses;
            var rolledPaths = rolled.Select(p => p.RecordPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var rolledGroup = item.Category == ItemCategory.Gear ? "Rolls on this component" : "Rolls on this item";
            return rolled.Select(p => new PartOptionViewModel(p, rolledGroup))
                .Concat(_catalog.GetParts(kind)
                    .Where(p => !rolledPaths.Contains(p.RecordPath))
                    .Select(p => new PartOptionViewModel(p, "All completion bonuses")))
                .ToList();
        }

        return _catalog.GetParts(kind)
            .Select(p => new PartOptionViewModel(p, kind switch
            {
                ItemPartKind.Prefix or ItemPartKind.Suffix => p.Rarity switch
                {
                    ItemRarity.Rare => $"Rare {kind.ToString().ToLowerInvariant()}es",
                    ItemRarity.Magical => $"Magical {kind.ToString().ToLowerInvariant()}es",
                    _ => $"Other {kind.ToString().ToLowerInvariant()}es"
                },
                ItemPartKind.Component => "Components",
                ItemPartKind.Augment => "Augments",
                _ => "Crafting bonuses"
            }))
            .OrderBy(o => o.GroupName.StartsWith("Rare", StringComparison.Ordinal) ? 0 : o.GroupName.StartsWith("Magical", StringComparison.Ordinal) ? 1 : 2)
            .ToList();
    }

    #endregion

    #region Seed and stack size

    /// <summary>The seed the game rolls the item's values from (the save only holds the seed).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SeedError), nameof(HasSeedError))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private string _seedText;

    partial void OnSeedTextChanged(string value) => RefreshPreview();

    public string? SeedError => TryParseSeed(out _) ? null : "Enter a whole number from 0 to 4,294,967,295.";

    public bool HasSeedError => SeedError is not null;

    /// <summary>A new random seed: the game rolls other values from it.</summary>
    [RelayCommand]
    private void RerollSeed() => SeedText = NewSeed().ToString(CultureInfo.InvariantCulture);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QuantityError), nameof(HasQuantityError))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private string _quantityText;

    partial void OnQuantityTextChanged(string value) => RefreshPreview();

    public string? QuantityError => !IsStackable || TryParseQuantity(out _) ? null : $"Enter a stack size from 1 to {MaxQuantity:N0}.";

    public bool HasQuantityError => QuantityError is not null;

    private bool TryParseSeed(out uint seed) =>
        uint.TryParse(SeedText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out seed);

    private bool TryParseQuantity(out uint quantity)
    {
        if (!IsStackable)
        {
            quantity = 1;
            return true;
        }

        return uint.TryParse(QuantityText.Trim().Replace(",", string.Empty).Replace(" ", string.Empty),
                   NumberStyles.None, CultureInfo.InvariantCulture, out quantity)
               && quantity is >= 1 and <= MaxQuantity;
    }

    /// <summary>Seeds the game makes are positive 31-bit numbers; the builder makes the same kind.</summary>
    private static uint NewSeed() => (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);

    #endregion

    #region Building

    /// <summary>The item as the builder would make it now; <c>null</c> while nothing is selected or a field is invalid.</summary>
    private SaveItem? BuildItem()
    {
        if (SelectedOption is not { } option || !TryParseSeed(out var seed) || !TryParseQuantity(out var quantity))
            return null;

        var record = option.Item.RecordPath;
        var item = _current is not null
            ? _current with { BaseRecord = record, Seed = seed }
            : SaveItem.Create(record, seed);

        var component = Record(ItemPartKind.Component);
        var augment = Record(ItemPartKind.Augment);
        item = item with
        {
            PrefixRecord = Record(ItemPartKind.Prefix),
            SuffixRecord = Record(ItemPartKind.Suffix),
            ModifierRecord = Record(ItemPartKind.CraftingBonus),
            ComponentRecord = component,
            CompletionBonusRecord = Record(ItemPartKind.CompletionBonus),
            AugmentRecord = augment,
            // A part keeps its seed while it stays the same; a new one gets a new seed, a removed one none.
            ComponentSeed = component.Length == 0 ? 0
                : SameRecord(_current?.ComponentRecord, component) ? _current!.ComponentSeed : NewSeed(),
            AugmentSeed = augment.Length == 0 ? 0
                : SameRecord(_current?.AugmentRecord, augment) ? _current!.AugmentSeed : NewSeed()
        };

        return item.WithQuantity(option.Item.IsStackable ? quantity : 1);

        string Record(ItemPartKind kind) =>
            PartSlots.First(s => s.Kind == kind) is { IsAvailable: true, Part: { } part } ? part.RecordPath : string.Empty;

        static bool SameRecord(string? a, string b) => string.Equals(a?.Replace('\\', '/'), b, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The game's tooltip of the item as built; <c>null</c> while nothing is selected.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview), nameof(HeaderName), nameof(HeaderType), nameof(HeaderIcon), nameof(HeaderRarity))]
    private EquipmentSlotViewModel? _preview;

    // ===== Header of the inspector: the item as built (the preview), the selection until the preview is ready =====

    /// <summary>"Mythical Ring of Steel of the Bear", or a prompt while nothing is selected.</summary>
    public string HeaderName => SelectedOption is null
        ? UnknownBaseText is null ? "Select an item" : "Unknown item"
        : Preview?.Name ?? SelectedOption.Name;

    /// <summary>"Double Rare Ring", "Component", ...</summary>
    public string HeaderType => SelectedOption is null ? "New item" : Preview?.TypeText ?? SelectedOption.Details;

    public System.Windows.Media.ImageSource? HeaderIcon => Preview?.Icon ?? SelectedOption?.Icon;

    /// <summary>The name color: affixes raise a common or magical base, like in the game.</summary>
    public ItemRarity HeaderRarity => Preview?.Rarity ?? SelectedOption?.Rarity ?? ItemRarity.Unknown;

    /// <summary>Value of the inspector's "Item" row.</summary>
    public string SelectedItemText => SelectedOption?.Name ?? "Choose an item";

    /// <summary>Value of the inspector's "Category" row.</summary>
    public string SelectedCategoryName => _selectedCategory.Name;

    /// <summary>Value of the inspector's "Rarity" row.</summary>
    public FilterOptionViewModel SelectedRarity => _selectedRarity;

    /// <summary>Filled part slots out of the available ones, e.g. "2 / 5" (value of the inspector's "Parts" row).</summary>
    public string PartSummary => $"{PartSlots.Count(s => s.IsAvailable && s.HasPart)} / {PartSlots.Count(s => s.IsAvailable)}";

    public bool HasPreview => Preview is not null;

    /// <summary>Builds the tooltip on a worker thread (database lookups); a newer request wins.</summary>
    private async void RefreshPreview()
    {
        var version = ++_previewVersion;
        if (BuildItem() is not { } item)
        {
            if (SelectedOption is null)
                Preview = null;
            return;
        }

        var preview = new EquipmentSlotViewModel(item, "Item");
        try
        {
            var apply = await Task.Run(() => preview.Resolve(_items, _icons));
            if (version != _previewVersion)
                return;

            apply();
            Preview = preview;
        }
        catch (Exception)
        {
            // Async void: nothing may escape. The preview keeps its previous state; building still works.
        }
    }

    private bool CanApply() => SelectedOption is not null && BuildItem() is not null && SelectedSize is not null;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply()
    {
        if (BuildItem() is not { } item || SelectedSize is not { } size)
            return;

        CloseRequested?.Invoke(this, new BuiltItem(item, size.Columns, size.Rows));
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, null);

    /// <summary>The builder edits an item of a bag, which can be removed.</summary>
    public bool CanRemove { get; }

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void Remove()
    {
        if (!_confirm("Remove item", "Remove this item from the bag? It is deleted from the save file when you save the character."))
            return;

        RemoveRequested?.Invoke(this, EventArgs.Empty);
        CloseRequested?.Invoke(this, null);
    }

    #endregion
}
