using System.Collections.ObjectModel;
using System.Globalization;
using GD.SaveEditor.Presentation.ViewModels.Characters;
using GD.SaveEditor.Services.GameData.Database;
using GD.SaveEditor.Services.GameData.Items;
using GD.SaveEditor.Services.GameData.Textures;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Presentation.ViewModels.Stash;

/// <summary>A shared file of one mode the main window writes on Save besides the character (shared stash, material storage).</summary>
public interface ISharedStashFileViewModel
{
    bool Hardcore { get; }

    /// <summary>"Shared stash (softcore)", "Material storage (hardcore)".</summary>
    string Title { get; }

    bool HasChanges { get; }
}

/// <summary>One material of the Smuggler's material storage: its icon and name, and the amount stored.</summary>
public sealed partial class MaterialViewModel : ViewModelBase
{
    public MaterialViewModel(string recordPath, int amount, bool isEditable)
    {
        RecordPath = recordPath;
        OriginalAmount = amount;
        IsEditable = isEditable;
        Item = new EquipmentSlotViewModel(SaveItem.Create(recordPath, 0), "Material");
        _amountText = Format(amount);
    }

    public string RecordPath { get; }

    /// <summary>Icon, name and tooltip from the game data.</summary>
    public EquipmentSlotViewModel Item { get; }

    /// <summary>Amount in the file (0 for a material the file does not list).</summary>
    public int OriginalAmount { get; }

    public bool IsEditable { get; }

    public bool IsReadOnly => !IsEditable;

    /// <summary>Text of the amount field.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Amount), nameof(IsChanged), nameof(HasError), nameof(ToolTipText))]
    private string _amountText;

    /// <summary>The typed amount; <c>null</c> when it is not a number from 0 to <see cref="MaterialStorage.MaxAmount"/>.</summary>
    public int? Amount =>
        int.TryParse(AmountText.Replace(",", "").Replace(" ", "").Replace(" ", ""), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
        && value <= MaterialStorage.MaxAmount
            ? value
            : string.IsNullOrWhiteSpace(AmountText) ? 0 : null;

    public bool HasError => Amount is null;

    /// <summary>A valid amount that differs from the file.</summary>
    public bool IsChanged => Amount is { } amount && amount != OriginalAmount;

    /// <summary>Shown or hidden by the search box.</summary>
    [ObservableProperty]
    private bool _isVisible = true;

    public string ToolTipText => HasError
        ? $"Enter a whole number from 0 to {MaterialStorage.MaxAmount:N0}."
        : IsChanged ? $"Stored in the file: {OriginalAmount:N0}" : "Amount stored (Up/Down by one, Shift by 100)";

    public void Reset() => AmountText = Format(OriginalAmount);

    private static string Format(int amount) => amount.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// The Smuggler's material storage of one mode (game version 1.3+, <c>reagents.gst</c> / <c>reagents.gsh</c>): the
/// shared "Components" and "Crafting materials" pages. Every material the file lists is shown, and every component and
/// crafting material of the game database (with 0) so it can be added. Amounts are written on Save.
/// </summary>
public sealed partial class MaterialStorageViewModel : ViewModelBase, ISharedStashFileViewModel
{
    private const string ComponentFolder = "records/items/materia/";
    private const string CraftingFolder = "records/items/crafting/materials/";

    /// <summary>Quest items the game keeps on the crafting materials page.</summary>
    private static readonly string[] CraftingQuestItems =
    [
        "records/items/questitems/quest_dynamite.dbr",
        "records/items/questitems/scrapmetal.dbr"
    ];

    private readonly IGameItemProvider _items;
    private readonly IGameIconProvider _icons;
    private readonly IItemCatalog _catalog;

    public MaterialStorageViewModel(MaterialStorageLoad load, IGameItemProvider items, IGameIconProvider icons, IItemCatalog catalog)
    {
        Load = load;
        _items = items;
        _icons = icons;
        _catalog = catalog;
        _ = LoadMaterialsAsync();
    }

    public MaterialStorageLoad Load { get; }

    public MaterialStorage? Storage => Load.Storage;

    public bool Hardcore => Load.Hardcore;

    public string Title => Load.Hardcore ? "Material storage (hardcore)" : "Material storage (softcore)";

    /// <summary>The storage was read: amounts can be changed.</summary>
    public bool CanEdit => Load.Storage is not null;

    /// <summary>"Components" page: <c>records/items/materia/</c>.</summary>
    public ObservableCollection<MaterialViewModel> Components { get; } = [];

    /// <summary>"Crafting materials" page: <c>records/items/crafting/materials/</c>, dynamite and scrap metal.</summary>
    public ObservableCollection<MaterialViewModel> CraftingMaterials { get; } = [];

    private IEnumerable<MaterialViewModel> AllMaterials => Components.Concat(CraftingMaterials);

    /// <summary>Names and icons are being read from the game data.</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Text of the search box: only materials whose name contains it are shown.</summary>
    [ObservableProperty]
    private string _filterText = string.Empty;

    public string? Notice =>
        Load.Storage is null ? $"The material storage could not be read: {Load.Error}"
        : $"Shared by every {(Load.Hardcore ? "hardcore" : "softcore")} character (the Smuggler's Components and Crafting Materials pages). Changes are written to {System.IO.Path.GetFileName(Load.Storage.FilePath)} on Save.";

    /// <summary>The database was not generated: only the materials the file lists are shown, without icons.</summary>
    public bool IsDatabaseMissing => !_catalog.IsAvailable;

    public bool HasChanges => AllMaterials.Any(m => m.IsChanged || m.HasError);

    /// <summary>Amounts to write by record path; <c>null</c> when nothing changed. An invalid amount is not written.</summary>
    public IReadOnlyDictionary<string, int>? ChangedAmounts
    {
        get
        {
            var changed = AllMaterials.Where(m => m.IsChanged).ToDictionary(m => m.RecordPath, m => m.Amount!.Value, StringComparer.OrdinalIgnoreCase);
            return changed.Count > 0 ? changed : null;
        }
    }

    /// <summary>Some amount is not a valid number: the storage cannot be saved until it is fixed.</summary>
    public bool HasErrors => AllMaterials.Any(m => m.HasError);

    /// <summary>Puts every amount of the file back.</summary>
    [RelayCommand]
    private void Reset()
    {
        foreach (var material in AllMaterials)
            material.Reset();
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var filter = FilterText.Trim();
        foreach (var material in AllMaterials)
        {
            material.IsVisible = filter.Length == 0
                                 || material.Item.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
                                 || material.RecordPath.Contains(filter, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Lists the materials of the file and of the database, then reads their names and icons on a worker thread. The
    /// pages are sorted by name, like the game's.
    /// </summary>
    private async Task LoadMaterialsAsync()
    {
        IsLoading = true;
        try
        {
            var stored = Load.Storage?.Materials ?? [];
            var list = await Task.Run(() =>
            {
                var amounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var material in stored)
                    amounts[material.RecordPath] = material.Amount;

                if (_catalog.IsAvailable)
                {
                    foreach (var item in _catalog.GetItems())
                    {
                        if (IsStorable(item) && !amounts.ContainsKey(item.RecordPath))
                            amounts[item.RecordPath] = 0;
                    }
                }

                var materials = amounts.Select(a => new MaterialViewModel(a.Key, a.Value, CanEdit)).ToList();
                var apply = materials.Select(m => m.Item.Resolve(_items, _icons)).ToList();
                return (materials, apply);
            });

            foreach (var action in list.apply)
                action();

            foreach (var material in list.materials.OrderBy(m => m.Item.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                material.PropertyChanged += OnMaterialPropertyChanged;
                (IsComponent(material.RecordPath) ? Components : CraftingMaterials).Add(material);
            }

            ApplyFilter();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException or UnauthorizedAccessException
                                       or System.Data.Common.DbException)
        {
            // The game data could not be read: the stored materials are listed by their record names, without icons.
            foreach (var material in Load.Storage?.Materials ?? [])
            {
                var view = new MaterialViewModel(material.RecordPath, material.Amount, CanEdit);
                view.PropertyChanged += OnMaterialPropertyChanged;
                (IsComponent(material.RecordPath) ? Components : CraftingMaterials).Add(view);
            }
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsDatabaseMissing));
        }
    }

    private void OnMaterialPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MaterialViewModel.IsChanged) or nameof(MaterialViewModel.HasError))
        {
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(HasErrors));
        }
    }

    private static bool IsComponent(string recordPath) => recordPath.StartsWith(ComponentFolder, StringComparison.OrdinalIgnoreCase);

    /// <summary>A database item the Smuggler stores as a material: a component or a crafting material.</summary>
    private static bool IsStorable(CatalogItem item) =>
        (item.Category == ItemCategory.Component && IsComponent(item.RecordPath))
        || item.RecordPath.StartsWith(CraftingFolder, StringComparison.OrdinalIgnoreCase)
        || CraftingQuestItems.Contains(item.RecordPath, StringComparer.OrdinalIgnoreCase);
}
