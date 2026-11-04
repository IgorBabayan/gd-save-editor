using System.Windows.Media.Imaging;
using GD.SaveEditor.Presentation.ViewModels.Characters;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Presentation.ViewModels.Inventory;

/// <summary>An item in a bag: where it is on the grid and how many cells it covers.</summary>
public sealed partial class InventoryItemViewModel : ViewModelBase
{
    /// <summary>Size of a bag cell in pixels; item icons are drawn at 32 pixels per cell, like in the game.</summary>
    public const double CellSize = 32;

    /// <summary>Largest item size in cells (two-handed weapons are 2 x 4).</summary>
    private const int MaxSpan = 4;

    public InventoryItemViewModel(InventoryItem item)
    {
        X = item.X;
        Y = item.Y;
        Item = new EquipmentSlotViewModel(item.Item, "Item");
        Item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EquipmentSlotViewModel.Icon))
                UpdateSize();
        };
    }

    /// <summary>Name, icon and tooltip of the item.</summary>
    public EquipmentSlotViewModel Item { get; }

    /// <summary>Top-left cell.</summary>
    public int X { get; }

    public int Y { get; }

    /// <summary>Cells covered across; 1 until the icon is known (its size gives the item's size).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Width))]
    private int _columns = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Height))]
    private int _rows = 1;

    public double Left => X * CellSize;

    public double Top => Y * CellSize;

    public double Width => Columns * CellSize;

    public double Height => Rows * CellSize;

    /// <summary>The game's item icons are 32 pixels per cell: a 64 x 96 helm covers 2 x 3 cells.</summary>
    private void UpdateSize()
    {
        if (Item.Icon is not BitmapSource bitmap)
            return;

        Columns = Math.Clamp((int)Math.Round(bitmap.PixelWidth / CellSize), 1, MaxSpan);
        Rows = Math.Clamp((int)Math.Round(bitmap.PixelHeight / CellSize), 1, MaxSpan);
    }
}

/// <summary>One bag of the inventory: its grid and the items on it.</summary>
public sealed partial class InventoryBagViewModel : ViewModelBase
{
    public InventoryBagViewModel(int index, string name, int columns, int rows, IReadOnlyList<InventoryItem> items)
    {
        Index = index;
        Name = name;
        Items = items.Select(i => new InventoryItemViewModel(i)).ToList();

        // An item outside the usual size (a bag the game made bigger) still gets a cell.
        MinColumns = Math.Max(columns, items.Select(i => i.X + 1).DefaultIfEmpty(0).Max());
        MinRows = Math.Max(rows, items.Select(i => i.Y + 1).DefaultIfEmpty(0).Max());
        foreach (var item in Items)
        {
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(InventoryItemViewModel.Columns))
                    OnPropertyChanged(nameof(PixelWidth));
                else if (e.PropertyName == nameof(InventoryItemViewModel.Rows))
                    OnPropertyChanged(nameof(PixelHeight));
            };
        }
    }

    /// <summary>Position of the bag in the save file: 0 is the main bag.</summary>
    public int Index { get; }

    /// <summary>"Main bag", "Bag 1", ...</summary>
    public string Name { get; }

    /// <summary>Bag number on its tab button: "1" for the first bag beside the main bag.</summary>
    public string Number => Index.ToString();

    public IReadOnlyList<InventoryItemViewModel> Items { get; }

    public int ItemCount => Items.Count;

    public bool IsEmpty => Items.Count == 0;

    /// <summary>"Bag 1 · 3 items" for the tab tooltip.</summary>
    public string Summary => IsEmpty ? $"{Name} · empty" : $"{Name} · {ItemCount} item{(ItemCount == 1 ? "" : "s")}";

    private int MinColumns { get; }

    private int MinRows { get; }

    /// <summary>Size of the grid: the bag's size, larger when an item reaches past it.</summary>
    public double PixelWidth => Math.Max(MinColumns, Items.Select(i => i.X + i.Columns).DefaultIfEmpty(0).Max()) * InventoryItemViewModel.CellSize;

    public double PixelHeight => Math.Max(MinRows, Items.Select(i => i.Y + i.Rows).DefaultIfEmpty(0).Max()) * InventoryItemViewModel.CellSize;
}
