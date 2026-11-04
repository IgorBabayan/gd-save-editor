using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using GD.SaveEditor.Presentation.ViewModels.Characters;
using GD.SaveEditor.Services.Saves;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Presentation.ViewModels.Inventory;

/// <summary>
/// An item in a bag: where it is on the grid, how many cells it covers, where it was read from, and what it is now (an
/// item changed in the item builder, or one made there).
/// </summary>
public sealed partial class InventoryItemViewModel : ViewModelBase
{
    /// <summary>Size of a bag cell in pixels; item icons are drawn at 32 pixels per cell, like in the game.</summary>
    public const double CellSize = 32;

    /// <summary>Largest item size in cells (two-handed weapons are 2 x 4).</summary>
    private const int MaxSpan = 4;

    /// <summary>Name, icon and tooltip of the item as read from the save file, kept to undo a change.</summary>
    private readonly EquipmentSlotViewModel? _originalView;

    /// <summary>An item of the save file.</summary>
    public InventoryItemViewModel(InventoryItem item, int fromBag, int fromIndex)
    {
        FromBag = fromBag;
        FromIndex = fromIndex;
        OriginalItem = item.Item;
        CurrentItem = item.Item;
        OriginalX = item.X;
        OriginalY = item.Y;
        X = item.X;
        Y = item.Y;
        _originalView = new EquipmentSlotViewModel(item.Item, "Item");
        SetView(_originalView);
    }

    /// <summary>An item made in the item builder, put at cell (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public InventoryItemViewModel(BuiltItem built, int x, int y)
    {
        FromBag = PlacedItem.New;
        FromIndex = PlacedItem.New;
        CurrentItem = built.Item;
        X = OriginalX = x;
        Y = OriginalY = y;
        SetView(new EquipmentSlotViewModel(built.Item, "Item"));
        SetSize(built.Columns, built.Rows);
    }

    /// <summary>Name, icon and tooltip of the item.</summary>
    [ObservableProperty]
    private EquipmentSlotViewModel _item = null!;

    /// <summary>Bag the item was read from (the save file's bag order); <see cref="PlacedItem.New"/> for a new item.</summary>
    public int FromBag { get; }

    /// <summary>Position of the item in that bag's list in the save file; <see cref="PlacedItem.New"/> for a new item.</summary>
    public int FromIndex { get; }

    /// <summary>The item as the save file has it; <c>null</c> for a new item.</summary>
    public SaveItem? OriginalItem { get; }

    /// <summary>The item as it will be written.</summary>
    public SaveItem CurrentItem { get; private set; }

    /// <summary>The item was made in the item builder: it is not in the save file yet.</summary>
    public bool IsNew => OriginalItem is null;

    /// <summary>An item of the save file was changed in the item builder.</summary>
    public bool IsEdited => OriginalItem is not null && !ReferenceEquals(CurrentItem, OriginalItem);

    public int OriginalX { get; }

    public int OriginalY { get; }

    /// <summary>
    /// Puts <paramref name="built"/> in place of the item; its size comes from the builder (the icon of the new item).
    /// Returns the view to resolve (name, icon, tooltip).
    /// </summary>
    public EquipmentSlotViewModel Replace(BuiltItem built)
    {
        CurrentItem = built.Item;
        var view = new EquipmentSlotViewModel(built.Item, "Item");
        SetView(view);
        SetSize(built.Columns, built.Rows);
        OnPropertyChanged(nameof(IsEdited));
        return view;
    }

    /// <summary>Puts back the item of the save file (its name, icon and size as read).</summary>
    public void Revert()
    {
        if (OriginalItem is null || _originalView is null)
            return;

        CurrentItem = OriginalItem;
        SetView(_originalView);
        if (_originalView.Icon is BitmapSource)
        {
            UpdateSize();
        }
        else
        {
            (Columns, Rows) = (1, 1);
            IsSizeKnown = false;
        }

        OnPropertyChanged(nameof(IsEdited));
    }

    private void SetView(EquipmentSlotViewModel view)
    {
        if (Item is not null)
            Item.PropertyChanged -= OnViewPropertyChanged;
        Item = view;
        view.PropertyChanged += OnViewPropertyChanged;
    }

    private void OnViewPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EquipmentSlotViewModel.Icon))
            UpdateSize();
    }

    private void SetSize(int columns, int rows)
    {
        Columns = Math.Clamp(columns, 1, MaxSpan);
        Rows = Math.Clamp(rows, 1, MaxSpan);
        IsSizeKnown = true;
    }

    /// <summary>Top-left cell.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Left))]
    private int _x;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Top))]
    private int _y;

    /// <summary>Cells covered across; 1 until the icon is known (its size gives the item's size).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Width))]
    private int _columns = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Height))]
    private int _rows = 1;

    /// <summary>
    /// The icon was read, so <see cref="Columns"/> and <see cref="Rows"/> are the item's real size. Until then the item
    /// cannot be moved: the editor could not tell which cells it covers.
    /// </summary>
    [ObservableProperty]
    private bool _isSizeKnown;

    /// <summary>The item is being dragged (drawn faded at its old place).</summary>
    [ObservableProperty]
    private bool _isDragging;

    public double Left => X * CellSize;

    public double Top => Y * CellSize;

    public double Width => Columns * CellSize;

    public double Height => Rows * CellSize;

    /// <summary>The game's item icons are 32 pixels per cell: a 64 x 96 helm covers 2 x 3 cells.</summary>
    private void UpdateSize()
    {
        if (Item.Icon is not BitmapSource bitmap)
            return;

        SetSize((int)Math.Round(bitmap.PixelWidth / CellSize), (int)Math.Round(bitmap.PixelHeight / CellSize));
    }
}

/// <summary>One bag of the inventory: its grid, the items on it and the drop preview while an item is dragged.</summary>
public sealed partial class InventoryBagViewModel : ViewModelBase
{
    public InventoryBagViewModel(int index, string name, int columns, int rows, IReadOnlyList<InventoryItem> items)
    {
        Index = index;
        Name = name;

        // An item outside the usual size (a bag the game made bigger) still gets a cell.
        Columns = Math.Max(columns, items.Select(i => i.X + 1).DefaultIfEmpty(0).Max());
        Rows = Math.Max(rows, items.Select(i => i.Y + 1).DefaultIfEmpty(0).Max());

        for (var i = 0; i < items.Count; i++)
            Add(new InventoryItemViewModel(items[i], index, i));

        Items.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ItemCount));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(Summary));
            OnSizeChanged();
        };
    }

    /// <summary>Position of the bag in the save file: 0 is the main bag.</summary>
    public int Index { get; }

    /// <summary>"Main bag", "Bag 1", ...</summary>
    public string Name { get; }

    /// <summary>Bag number on its tab button: "1" for the first bag beside the main bag.</summary>
    public string Number => Index.ToString();

    public ObservableCollection<InventoryItemViewModel> Items { get; } = [];

    public int ItemCount => Items.Count;

    public bool IsEmpty => Items.Count == 0;

    /// <summary>"Bag 1 · 3 items" for the tab tooltip.</summary>
    public string Summary => IsEmpty ? $"{Name} · empty" : $"{Name} · {ItemCount} item{(ItemCount == 1 ? "" : "s")}";

    /// <summary>Cells across: items can be put in columns 0 to <c>Columns - 1</c>.</summary>
    public int Columns { get; }

    public int Rows { get; }

    /// <summary>Size of the grid: the bag's size, larger when an item reaches past it.</summary>
    public double PixelWidth => Math.Max(Columns, Items.Select(i => i.X + i.Columns).DefaultIfEmpty(0).Max()) * InventoryItemViewModel.CellSize;

    public double PixelHeight => Math.Max(Rows, Items.Select(i => i.Y + i.Rows).DefaultIfEmpty(0).Max()) * InventoryItemViewModel.CellSize;

    #region Drop preview

    /// <summary>The cells an item would cover if dropped now; shown green when it fits, red when it does not.</summary>
    [ObservableProperty]
    private bool _isPreviewVisible;

    [ObservableProperty]
    private bool _isPreviewValid;

    [ObservableProperty]
    private double _previewLeft;

    [ObservableProperty]
    private double _previewTop;

    [ObservableProperty]
    private double _previewWidth;

    [ObservableProperty]
    private double _previewHeight;

    public void ShowPreview(InventoryItemViewModel item, int x, int y)
    {
        PreviewLeft = Math.Max(0, x) * InventoryItemViewModel.CellSize;
        PreviewTop = Math.Max(0, y) * InventoryItemViewModel.CellSize;
        PreviewWidth = item.Columns * InventoryItemViewModel.CellSize;
        PreviewHeight = item.Rows * InventoryItemViewModel.CellSize;
        IsPreviewValid = CanPlace(item, x, y);
        IsPreviewVisible = true;
    }

    public void HidePreview() => IsPreviewVisible = false;

    #endregion

    /// <summary>
    /// <paramref name="item"/> fits at cell (<paramref name="x"/>, <paramref name="y"/>): inside the bag and over no
    /// other item. The item itself does not count, so it can be moved by a cell onto its own old place.
    /// </summary>
    public bool CanPlace(InventoryItemViewModel item, int x, int y) =>
        item.IsSizeKnown && CanPlace(item.Columns, item.Rows, x, y, item);

    /// <summary>
    /// An item of <paramref name="columns"/> x <paramref name="rows"/> cells fits at cell (<paramref name="x"/>,
    /// <paramref name="y"/>): inside the bag and over no other item than <paramref name="ignore"/> (the item itself
    /// when it is moved or changed, so it can take its own old cells).
    /// </summary>
    public bool CanPlace(int columns, int rows, int x, int y, InventoryItemViewModel? ignore = null)
    {
        if (x < 0 || y < 0 || x + columns > Columns || y + rows > Rows)
            return false;

        foreach (var other in Items)
        {
            if (ReferenceEquals(other, ignore))
                continue;

            // Without its size an item could reach under the placed one.
            if (!other.IsSizeKnown)
                return false;

            if (x < other.X + other.Columns && other.X < x + columns
                && y < other.Y + other.Rows && other.Y < y + rows)
                return false;
        }

        return true;
    }

    /// <summary>First cell, row by row, where <paramref name="item"/> fits; <c>null</c> when the bag has no room for it.</summary>
    public (int X, int Y)? FindFreeCell(InventoryItemViewModel item) =>
        item.IsSizeKnown ? FindFreeCell(item.Columns, item.Rows, item) : null;

    /// <summary>First cell, row by row, where an item of that size fits; <c>null</c> when the bag has no room for it.</summary>
    public (int X, int Y)? FindFreeCell(int columns, int rows, InventoryItemViewModel? ignore = null)
    {
        for (var y = 0; y < Rows; y++)
        {
            for (var x = 0; x < Columns; x++)
            {
                if (CanPlace(columns, rows, x, y, ignore))
                    return (x, y);
            }
        }

        return null;
    }

    /// <summary>The cell under a point of the grid, in pixels from its top-left corner.</summary>
    public static (int X, int Y) CellAt(double left, double top) =>
        ((int)Math.Floor(left / InventoryItemViewModel.CellSize), (int)Math.Floor(top / InventoryItemViewModel.CellSize));

    public void Add(InventoryItemViewModel item)
    {
        Items.Add(item);
        item.PropertyChanged += OnItemPropertyChanged;
    }

    public void Remove(InventoryItemViewModel item)
    {
        item.PropertyChanged -= OnItemPropertyChanged;
        Items.Remove(item);
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InventoryItemViewModel.X) or nameof(InventoryItemViewModel.Y)
            or nameof(InventoryItemViewModel.Columns) or nameof(InventoryItemViewModel.Rows))
            OnSizeChanged();
    }

    private void OnSizeChanged()
    {
        OnPropertyChanged(nameof(PixelWidth));
        OnPropertyChanged(nameof(PixelHeight));
    }
}
