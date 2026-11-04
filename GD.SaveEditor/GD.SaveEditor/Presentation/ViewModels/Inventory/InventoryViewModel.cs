using System.Globalization;
using System.Text;
using GD.SaveEditor.Presentation.Services;
using GD.SaveEditor.Presentation.ViewModels.Characters;
using GD.SaveEditor.Services.GameData.Database;
using GD.SaveEditor.Services.GameData.Textures;
using GD.SaveEditor.Services.Saves;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Presentation.ViewModels.Inventory;

/// <summary>
/// The inventory shown under the equipment, laid out like the game's: the main bag on the left with the iron bits
/// above it, the other bags on the right behind their tab buttons. The iron bits can be changed; items can be dragged
/// to another place in their bag or into another bag, made in the item builder, changed there, or removed.
/// </summary>
public sealed partial class InventoryViewModel : ViewModelBase
{
    private const int MainBagColumns = 12;
    private const int MainBagRows = 8;
    private const int BagColumns = 8;
    private const int BagRows = 8;

    /// <summary>Largest amount of iron bits accepted; the game keeps them in a signed 32-bit number.</summary>
    public const uint MaxMoney = int.MaxValue;

    private readonly IGameItemProvider _items;
    private readonly IGameIconProvider _icons;
    private readonly IDialogService _dialogs;

    /// <summary>Items of the save file that were removed, to put them back with <see cref="ResetBagsCommand"/>.</summary>
    private readonly List<(InventoryItemViewModel Item, InventoryBagViewModel Bag)> _removed = [];

    public InventoryViewModel(CharacterSave character, IGameItemProvider items, IGameIconProvider icons, IDialogService dialogs)
    {
        Character = character;
        _items = items;
        _icons = icons;
        _dialogs = dialogs;

        var bags = character.Inventory?.Bags ?? [];
        MainBag = new InventoryBagViewModel(0, "Main bag", MainBagColumns, MainBagRows, bags.Count > 0 ? bags[0].Items : []);
        Bags = bags.Skip(1).Select((bag, i) => new InventoryBagViewModel(i + 1, $"Bag {i + 1}", BagColumns, BagRows, bag.Items)).ToList();
        SelectedBag = Bags.FirstOrDefault(b => b.Index == character.Inventory?.SelectedBag) ?? Bags.FirstOrDefault();
        MoneyText = FormatMoney(character.Money ?? 0);

        _ = LoadItemsAsync();
    }

    public CharacterSave Character { get; }

    /// <summary>The bags were read from the save file.</summary>
    public bool HasInventory => Character.Inventory is not null;

    public InventoryBagViewModel MainBag { get; }

    /// <summary>Bags beside the main bag, picked with the tab buttons.</summary>
    public IReadOnlyList<InventoryBagViewModel> Bags { get; }

    public bool HasBags => Bags.Count > 0;

    /// <summary>Bag shown on the right.</summary>
    [ObservableProperty]
    private InventoryBagViewModel? _selectedBag;

    /// <summary>Names and icons are being read from the game data.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MoveHint), nameof(HasMoveHint))]
    private bool _isLoading;

    /// <summary>Something was changed that is not written to the save file yet.</summary>
    public bool HasChanges => IsMoneyChanged || AreBagsChanged;

    #region Moving items

    /// <summary>The main bag, then the other bags, in the save file's order.</summary>
    private IEnumerable<InventoryBagViewModel> AllBags => Bags.Prepend(MainBag);

    private IEnumerable<InventoryItemViewModel> AllItems => AllBags.SelectMany(b => b.Items);

    /// <summary>
    /// Items can be moved, added, changed and removed: the bags were read (the file has at least one) and the editor can
    /// write the save file back.
    /// </summary>
    public bool CanMoveItems => HasInventory && Character.CanRewrite && Character.Inventory?.Bags.Count > 0;

    /// <summary>Same as <see cref="CanMoveItems"/>, for the "Add item" button and the bag menus.</summary>
    public bool CanEditItems => CanMoveItems;

    /// <summary>Why items cannot be moved (or some of them); <c>null</c> when they all can.</summary>
    public string? MoveHint =>
        !HasInventory || IsLoading ? null
        : !Character.CanRewrite ? $"Items cannot be moved, added or changed for this character: {Character.RewriteError}"
        : AllItems.Any(i => !i.IsSizeKnown)
            ? "Items without an icon cannot be moved: their size comes from the icon. Generate the game database (database button in the title bar) to move them."
            : null;

    public bool HasMoveHint => MoveHint is not null;

    /// <summary>An item can be picked up: its size is known, so the cells it covers are known.</summary>
    public bool CanDrag(InventoryItemViewModel item) => CanMoveItems && item.IsSizeKnown;

    /// <summary>
    /// Puts <paramref name="item"/> on <paramref name="target"/> with its top-left corner at cell
    /// (<paramref name="x"/>, <paramref name="y"/>); <c>false</c> (nothing changes) when it does not fit there.
    /// </summary>
    public bool MoveItem(InventoryItemViewModel item, InventoryBagViewModel target, int x, int y)
    {
        if (!CanDrag(item) || !target.CanPlace(item, x, y))
            return false;

        var source = AllBags.First(b => b.Items.Contains(item));
        if (!ReferenceEquals(source, target))
        {
            source.Remove(item);
            target.Add(item);
        }

        item.X = x;
        item.Y = y;
        OnBagsChanged();
        return true;
    }

    /// <summary>
    /// Something in the bags differs from the save file: an item was moved, added, changed in the item builder or
    /// removed.
    /// </summary>
    public bool AreBagsChanged =>
        _removed.Count > 0
        || AllBags.Any(b => b.Items.Any(i => i.IsNew || i.IsEdited || i.FromBag != b.Index || i.X != i.OriginalX || i.Y != i.OriginalY));

    /// <summary>
    /// The contents of every bag to write into the save file; <c>null</c> when the bags are as in the file. Items that
    /// stayed in their bag keep their order, items moved or added come after them; removed items are left out. A new or
    /// changed item carries its contents, an unchanged one only its cell.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<PlacedItem>>? ChangedBags => AreBagsChanged
        ? AllBags.Select(b => (IReadOnlyList<PlacedItem>)b.Items
                .Select(i => new PlacedItem(i.FromBag, i.FromIndex, i.X, i.Y, i.IsNew || i.IsEdited ? i.CurrentItem : null))
                .ToList())
            .ToList()
        : null;

    /// <summary>Puts the bags back as the save file has them: moved items return, new ones go, changed and removed ones come back.</summary>
    [RelayCommand]
    private void ResetBags()
    {
        foreach (var (item, bag) in _removed)
            bag.Add(item);
        _removed.Clear();

        var items = AllItems.ToList();
        foreach (var bag in AllBags)
        {
            foreach (var item in bag.Items.ToList())
                bag.Remove(item);
        }

        foreach (var item in items.Where(i => !i.IsNew).OrderBy(i => i.FromBag).ThenBy(i => i.FromIndex))
        {
            item.Revert();
            item.X = item.OriginalX;
            item.Y = item.OriginalY;
            AllBags.First(b => b.Index == item.FromBag).Add(item);
        }

        StatusMessage = null;
        OnBagsChanged();
    }

    private void OnBagsChanged()
    {
        OnPropertyChanged(nameof(AreBagsChanged));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(MoveHint));
        OnPropertyChanged(nameof(HasMoveHint));
    }

    #endregion

    #region Adding, changing and removing items

    /// <summary>What the last add / change could not do (no room in the bags); <c>null</c> when it worked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string? _statusMessage;

    public bool HasStatusMessage => StatusMessage is not null;

    /// <summary>"Add item" button: a new item at the first free cell of the main bag, then of the other bags.</summary>
    [RelayCommand]
    private void NewItem() => AddItem(MainBag, null);

    /// <summary>
    /// Opens the item builder for a new item and puts it on <paramref name="bag"/>: at <paramref name="cell"/> when it
    /// fits there, otherwise at the first free cell of that bag, then of the other bags.
    /// </summary>
    public void AddItem(InventoryBagViewModel bag, (int X, int Y)? cell)
    {
        if (!CanEditItems)
            return;

        StatusMessage = null;
        if (_dialogs.ShowItemBuilder(null).Item is not { } built)
            return;

        if (FindPlace(built.Columns, built.Rows, bag, cell, ignore: null) is not { } place)
        {
            StatusMessage = $"There is no free room of {built.Columns} x {built.Rows} cells in the bags for the new item. Make room and add it again.";
            return;
        }

        var item = new InventoryItemViewModel(built, place.Cell.X, place.Cell.Y);
        place.Bag.Add(item);
        if (place.Bag != MainBag)
            SelectedBag = place.Bag;

        _ = ResolveAsync(item.Item);
        OnBagsChanged();
    }

    /// <summary>
    /// Opens the item builder on <paramref name="item"/>. The changed item stays where it is when it still fits there
    /// (it may be larger now), otherwise it moves to the first free cell of its bag, then of the other bags; removing it
    /// in the builder takes it out of its bag.
    /// </summary>
    public void EditItem(InventoryItemViewModel item)
    {
        if (!CanEditItems || AllBags.FirstOrDefault(b => b.Items.Contains(item)) is not { } bag)
            return;

        StatusMessage = null;
        var result = _dialogs.ShowItemBuilder(item.CurrentItem);
        if (result.Removed)
        {
            Remove(item, bag);
            return;
        }

        if (result.Item is not { } built)
            return;

        if (FindPlace(built.Columns, built.Rows, bag, (item.X, item.Y), ignore: item) is not { } place)
        {
            StatusMessage = $"There is no free room of {built.Columns} x {built.Rows} cells in the bags for the changed item, so it was not changed. Make room and change it again.";
            return;
        }

        var view = item.Replace(built);
        if (!ReferenceEquals(place.Bag, bag))
        {
            bag.Remove(item);
            place.Bag.Add(item);
        }

        item.X = place.Cell.X;
        item.Y = place.Cell.Y;
        _ = ResolveAsync(view);
        OnBagsChanged();
    }

    /// <summary>Removes <paramref name="item"/> from its bag after asking.</summary>
    public void RemoveItem(InventoryItemViewModel item)
    {
        if (!CanEditItems || AllBags.FirstOrDefault(b => b.Items.Contains(item)) is not { } bag)
            return;

        if (!_dialogs.Confirm("Remove item", $"Remove {item.Item.Name} from the bag? It is deleted from the save file when you save the character."))
            return;

        Remove(item, bag);
    }

    private void Remove(InventoryItemViewModel item, InventoryBagViewModel bag)
    {
        bag.Remove(item);
        // An item of the file can be put back by the undo button; a new one is simply gone.
        if (!item.IsNew)
            _removed.Add((item, AllBags.First(b => b.Index == item.FromBag)));

        StatusMessage = null;
        OnBagsChanged();
    }

    /// <summary>
    /// Where an item of that size goes: the preferred cell of <paramref name="bag"/>, its first free cell, then the first
    /// free cell of the other bags (main bag first).
    /// </summary>
    private (InventoryBagViewModel Bag, (int X, int Y) Cell)? FindPlace(
        int columns, int rows, InventoryBagViewModel bag, (int X, int Y)? cell, InventoryItemViewModel? ignore)
    {
        if (cell is { } preferred && bag.CanPlace(columns, rows, preferred.X, preferred.Y, ignore))
            return (bag, preferred);

        foreach (var candidate in AllBags.Where(b => b != bag).Prepend(bag))
        {
            if (candidate.FindFreeCell(columns, rows, ignore) is { } free)
                return (candidate, free);
        }

        return null;
    }

    /// <summary>Reads the name, icon and tooltip of a new or changed item on a worker thread.</summary>
    private async Task ResolveAsync(EquipmentSlotViewModel view)
    {
        try
        {
            var apply = await Task.Run(() => view.Resolve(_items, _icons));
            apply();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
        {
            // The item keeps its record name; it is still written.
        }
    }

    #endregion

    #region Iron bits

    /// <summary>The iron bits were read from the save file.</summary>
    public bool HasMoney => Character.Money is not null;

    /// <summary>The iron bits can be changed: the editor knows every part of the save file, so it can write it back.</summary>
    public bool CanEditMoney => HasMoney && Character.CanRewrite;

    public bool IsMoneyReadOnly => !CanEditMoney;

    /// <summary>Tooltip of the iron bits field.</summary>
    public string MoneyToolTip => CanEditMoney
        ? $"Iron bits. Type a new amount (0 to {FormatMoney(MaxMoney)}), then save the character."
        : $"Iron bits. They cannot be changed for this character: {Character.RewriteError ?? "they could not be read."}";

    /// <summary>Text of the iron bits field.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MoneyError), nameof(HasMoneyError), nameof(IsMoneyChanged), nameof(ChangedMoney), nameof(HasChanges))]
    private string _moneyText = string.Empty;

    /// <summary>Why <see cref="MoneyText"/> is not a valid amount; <c>null</c> when it is.</summary>
    public string? MoneyError => !CanEditMoney || TryParseMoney(MoneyText, out _)
        ? null
        : $"Enter a whole number from 0 to {FormatMoney(MaxMoney)}.";

    public bool HasMoneyError => MoneyError is not null;

    /// <summary>The field holds a valid amount that differs from the save file.</summary>
    public bool IsMoneyChanged => ChangedMoney is not null;

    /// <summary>The new amount of iron bits; <c>null</c> when it is unchanged or not valid.</summary>
    public uint? ChangedMoney =>
        CanEditMoney && TryParseMoney(MoneyText, out var money) && money != Character.Money ? money : null;

    /// <summary>Puts the amount of the save file back.</summary>
    [RelayCommand]
    private void ResetMoney() => MoneyText = FormatMoney(Character.Money ?? 0);

    /// <summary>Shows a valid amount with group separators ("85,458,538"), e.g. when the field loses focus.</summary>
    public void NormalizeMoneyText()
    {
        if (TryParseMoney(MoneyText, out var money))
            MoneyText = FormatMoney(money);
    }

    /// <summary>Digits with optional group separators of any culture ("85,458,538", "85 458 538", "85.458.538").</summary>
    public static bool TryParseMoney(string? text, out uint money)
    {
        money = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var digits = new StringBuilder(text.Length);
        foreach (var c in text.Trim())
        {
            if (char.IsAsciiDigit(c))
                digits.Append(c);
            else if (c is not (',' or '.' or '\'' or '_') && !char.IsWhiteSpace(c))
                return false;
        }

        if (digits.Length is 0 or > 10
            || !ulong.TryParse(digits.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            || value > MaxMoney)
            return false;

        money = (uint)value;
        return true;
    }

    private static string FormatMoney(uint money) => money.ToString("N0", CultureInfo.CurrentCulture);

    #endregion

    /// <summary>Reads names and icons on a worker thread (database and archive access), then shows them.</summary>
    private async Task LoadItemsAsync()
    {
        var items = Bags.Prepend(MainBag).SelectMany(b => b.Items).Select(i => i.Item).ToList();
        if (items.Count == 0)
            return;

        IsLoading = true;
        try
        {
            var apply = await Task.Run(() => items.Select(i => i.Resolve(_items, _icons)).ToList());
            foreach (var action in apply)
                action();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
        {
            // Names stay the record names; the bags still show what they hold.
        }
        finally
        {
            IsLoading = false;
        }
    }
}
