using GD.SaveEditor.Presentation.Services;
using GD.SaveEditor.Presentation.ViewModels.Characters;
using GD.SaveEditor.Services.GameData.Database;
using GD.SaveEditor.Services.GameData.Items;
using GD.SaveEditor.Services.GameData.Textures;
using GD.SaveEditor.Services.Saves;

namespace GD.SaveEditor.Presentation.ViewModels.Inventory;

/// <summary>
/// Item grids that are written into one block of a save file: the bags of the inventory, the tabs of the personal
/// stash or the sacks of the shared stash. Items can be dragged to another cell or container, made in the item builder,
/// changed there, or removed; the undo button puts every container back as the file has it.
/// </summary>
public abstract partial class ItemGridViewModel : ViewModelBase
{
    private readonly IGameItemProvider _items;
    private readonly IGameIconProvider _icons;

    /// <summary>Items of the save file that were removed, to put them back with <see cref="ResetItemsCommand"/>.</summary>
    private readonly List<(InventoryItemViewModel Item, InventoryBagViewModel Bag)> _removed = [];

    protected ItemGridViewModel(IGameItemProvider items, IGameIconProvider icons, IDialogService dialogs)
    {
        _items = items;
        _icons = icons;
        Dialogs = dialogs;
    }

    protected IDialogService Dialogs { get; }

    /// <summary>Every container shown, in the file's order; tabs that were not bought are among them, but locked.</summary>
    public abstract IReadOnlyList<InventoryBagViewModel> Containers { get; }

    /// <summary>Where the "Add item" button puts a new item first.</summary>
    protected abstract InventoryBagViewModel? DefaultContainer { get; }

    /// <summary>"bags", "stash tabs", for the messages.</summary>
    protected abstract string ContainerNoun { get; }

    /// <summary>The containers were read from the file.</summary>
    public abstract bool HasItemData { get; }

    /// <summary>Why the items cannot be changed although they were read; <c>null</c> when they can.</summary>
    protected abstract string? ReadOnlyReason { get; }

    /// <summary>Items can be moved, added, changed and removed.</summary>
    public abstract bool CanEditItems { get; }

    /// <summary>Containers that are in the file (bought), in the file's order: the ones written back.</summary>
    protected IEnumerable<InventoryBagViewModel> AllBags => Containers.Where(c => c.IsAvailable);

    private IEnumerable<InventoryItemViewModel> AllItems => AllBags.SelectMany(b => b.Items);

    /// <summary>Names and icons are being read from the game data.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MoveHint), nameof(HasMoveHint))]
    private bool _isLoading;

    /// <summary>Something was changed that is not written to the file yet.</summary>
    public virtual bool HasChanges => AreItemsChanged;

    /// <summary>Shows <paramref name="container"/> (a tab button was picked, or a new item went there).</summary>
    public virtual void SelectContainer(InventoryBagViewModel container)
    {
    }

    #region Moving items

    /// <summary>Why items cannot be moved (or some of them); <c>null</c> when they all can.</summary>
    public string? MoveHint =>
        !HasItemData || IsLoading ? null
        : ReadOnlyReason is { } reason ? reason
        : AllItems.Any(i => !i.IsSizeKnown)
            ? "Items without an icon cannot be moved: their size comes from the icon. Generate the game database (database button in the title bar) to move them."
            : null;

    public bool HasMoveHint => MoveHint is not null;

    /// <summary><paramref name="item"/> is in one of these containers (and not in another grid).</summary>
    public bool Owns(InventoryItemViewModel item) => AllBags.Any(b => b.Items.Contains(item));

    /// <summary>An item can be picked up: its size is known, so the cells it covers are known.</summary>
    public bool CanDrag(InventoryItemViewModel item) => CanEditItems && item.IsSizeKnown && Owns(item);

    /// <summary>
    /// Puts <paramref name="item"/> on <paramref name="target"/> with its top-left corner at cell
    /// (<paramref name="x"/>, <paramref name="y"/>); <c>false</c> (nothing changes) when it does not fit there.
    /// </summary>
    public bool MoveItem(InventoryItemViewModel item, InventoryBagViewModel target, int x, int y)
    {
        if (!CanDrag(item) || !AllBags.Contains(target) || !target.CanPlace(item, x, y))
            return false;

        var source = AllBags.First(b => b.Items.Contains(item));
        if (!ReferenceEquals(source, target))
        {
            source.Remove(item);
            target.Add(item);
        }

        item.X = x;
        item.Y = y;
        OnItemsChanged();
        return true;
    }

    /// <summary>
    /// Something in the containers differs from the file: an item was moved, added, changed in the item builder or
    /// removed.
    /// </summary>
    public bool AreItemsChanged =>
        _removed.Count > 0
        || AllBags.Any(b => b.Items.Any(i => i.IsNew || i.IsEdited || i.FromBag != b.Index || i.X != i.OriginalX || i.Y != i.OriginalY));

    /// <summary>
    /// The contents of every container in the file to write; <c>null</c> when they are as in the file. Items that stayed
    /// in their container keep their order, items moved or added come after them; removed items are left out. A new or
    /// changed item carries its contents, an unchanged one only its cell.
    /// </summary>
    protected IReadOnlyList<IReadOnlyList<PlacedItem>>? ChangedContainers => AreItemsChanged
        ? AllBags.Select(b => (IReadOnlyList<PlacedItem>)b.Items
                .Select(i => new PlacedItem(i.FromBag, i.FromIndex, i.X, i.Y, i.IsNew || i.IsEdited ? i.CurrentItem : null))
                .ToList())
            .ToList()
        : null;

    /// <summary>Puts the containers back as the file has them: moved items return, new ones go, changed and removed ones come back.</summary>
    [RelayCommand]
    private void ResetItems()
    {
        // An open inspector would edit an item that is about to be put back.
        Dialogs.CloseItemInspector();

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
        OnItemsChanged();
    }

    protected virtual void OnItemsChanged()
    {
        OnPropertyChanged(nameof(AreItemsChanged));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(MoveHint));
        OnPropertyChanged(nameof(HasMoveHint));
    }

    #endregion

    #region Adding, changing and removing items

    /// <summary>What the last add / change could not do (no room); <c>null</c> when it worked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string? _statusMessage;

    public bool HasStatusMessage => StatusMessage is not null;

    /// <summary>
    /// "Add item" button: the item inspector opens next to the button (the command parameter); the new item goes to the
    /// first free cell of <see cref="DefaultContainer"/>, then of the other containers.
    /// </summary>
    [RelayCommand]
    private void NewItem(object? anchor)
    {
        if (DefaultContainer is { } container)
            AddItem(container, null, anchor);
    }

    /// <summary>
    /// Opens the item inspector for a new item next to <paramref name="anchor"/>, listing only items of
    /// <paramref name="kind"/> when one was picked in the "Build a new item" menu. Applied, the item goes on
    /// <paramref name="bag"/>: at <paramref name="cell"/> when it fits there, otherwise at the first free cell of that
    /// container, then of the others.
    /// </summary>
    public void AddItem(InventoryBagViewModel bag, (int X, int Y)? cell, object? anchor = null, NewItemKind? kind = null)
    {
        if (!CanEditItems || !AllBags.Contains(bag))
            return;

        StatusMessage = null;
        Dialogs.ShowItemInspector(anchor, null, built => PlaceNewItem(built, bag, cell), kind: kind);
    }

    /// <summary>
    /// Some container has a free cell. The size of an item is only known once it is picked (from its icon), so the menu's
    /// entries are disabled only when every container is full; a picked item without room gets a message.
    /// </summary>
    public bool HasFreeCell => AllBags.Any(b => b.FindFreeCell(1, 1) is not null);

    private void PlaceNewItem(BuiltItem built, InventoryBagViewModel bag, (int X, int Y)? cell)
    {
        if (FindPlace(built.Columns, built.Rows, bag, cell, ignore: null) is not { } place)
        {
            StatusMessage = $"There is no free room of {built.Columns} x {built.Rows} cells in the {ContainerNoun} for the new item. Make room and add it again.";
            return;
        }

        var item = new InventoryItemViewModel(built, place.Cell.X, place.Cell.Y);
        place.Bag.Add(item);
        SelectContainer(place.Bag);

        _ = ResolveAsync(item.Item);
        OnItemsChanged();
    }

    /// <summary>
    /// Opens the item inspector on <paramref name="item"/>, next to <paramref name="anchor"/> (the item in the grid).
    /// Applied, the changed item stays where it is when it still fits there (it may be larger now), otherwise it moves
    /// to the first free cell of its container, then of the others; removing it there takes it out of its container.
    /// </summary>
    public void EditItem(InventoryItemViewModel item, object? anchor = null)
    {
        if (!CanEditItems || !Owns(item))
            return;

        StatusMessage = null;
        Dialogs.ShowItemInspector(anchor, item.CurrentItem,
            built => ApplyEdit(item, built),
            () =>
            {
                if (AllBags.FirstOrDefault(b => b.Items.Contains(item)) is { } bag)
                    Remove(item, bag);
            });
    }

    private void ApplyEdit(InventoryItemViewModel item, BuiltItem built)
    {
        if (AllBags.FirstOrDefault(b => b.Items.Contains(item)) is not { } bag)
            return;

        if (FindPlace(built.Columns, built.Rows, bag, (item.X, item.Y), ignore: item) is not { } place)
        {
            StatusMessage = $"There is no free room of {built.Columns} x {built.Rows} cells in the {ContainerNoun} for the changed item, so it was not changed. Make room and change it again.";
            return;
        }

        var view = item.Replace(built);
        if (!ReferenceEquals(place.Bag, bag))
        {
            bag.Remove(item);
            place.Bag.Add(item);
            SelectContainer(place.Bag);
        }

        item.X = place.Cell.X;
        item.Y = place.Cell.Y;
        _ = ResolveAsync(view);
        OnItemsChanged();
    }

    /// <summary>Removes <paramref name="item"/> from its container after asking.</summary>
    public void RemoveItem(InventoryItemViewModel item)
    {
        if (!CanEditItems || AllBags.FirstOrDefault(b => b.Items.Contains(item)) is not { } bag)
            return;

        if (!Dialogs.Confirm("Remove item", $"Remove {item.Item.Name} from {bag.Name}? It is deleted from the save file when you save."))
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
        OnItemsChanged();
    }

    /// <summary>
    /// Where an item of that size goes: the preferred cell of <paramref name="bag"/>, its first free cell, then the first
    /// free cell of the other containers (in their order).
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

    /// <summary>Reads names and icons on a worker thread (database and archive access), then shows them.</summary>
    protected async Task LoadItemsAsync()
    {
        var items = AllItems.Select(i => i.Item).ToList();
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
            // Names stay the record names; the containers still show what they hold.
        }
        finally
        {
            IsLoading = false;
        }
    }
}
