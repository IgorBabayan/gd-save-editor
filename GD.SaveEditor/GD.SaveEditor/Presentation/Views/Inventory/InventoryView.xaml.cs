using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GD.SaveEditor.Presentation.ViewModels.Inventory;
using GD.SaveEditor.Services.GameData.Items;
using Material.Icons;
using Material.Icons.WPF;

namespace GD.SaveEditor.Presentation.Views.Inventory;

/// <summary>
/// Bags and iron bits of the selected character, shown under the equipment in the "Character" tab. Items are moved by
/// drag and drop: onto a cell of the main bag or of the shown bag, or onto a bag's tab button (first free cell; holding
/// an item over a tab also shows that bag). A click on an item opens the item inspector next to it (the compact item
/// builder; its expand button opens the full one). A right-click on a free cell offers to build a new item of a kind
/// (helm, weapon, ring, relic, ...) at that cell; on an item it offers to change or remove it.
/// </summary>
public partial class InventoryView : UserControl
{
    private const string DragFormat = "GD.SaveEditor.InventoryItem";

    /// <summary>Item under the mouse since the left button went down on it; dragging starts once the mouse moves.</summary>
    private InventoryItemViewModel? _pressedItem;

    private Point _pressPoint;

    /// <summary>Where the item was grabbed, from its top-left corner: the item keeps that point under the mouse.</summary>
    private Point _grabOffset;

    public InventoryView()
    {
        InitializeComponent();
    }

    private InventoryViewModel? ViewModel => DataContext as InventoryViewModel;

    #region Iron bits

    /// <summary>Shows the typed amount with group separators once the field is left.</summary>
    private void OnMoneyLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => ViewModel?.NormalizeMoneyText();

    /// <summary>Enter formats the amount, Esc puts back the amount of the save file.</summary>
    private void OnMoneyPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is not { } viewModel || sender is not TextBox textBox)
            return;

        switch (e.Key)
        {
            case Key.Enter:
                viewModel.NormalizeMoneyText();
                textBox.CaretIndex = textBox.Text.Length;
                e.Handled = true;
                break;
            case Key.Escape when viewModel.IsMoneyChanged:
                viewModel.ResetMoneyCommand.Execute(null);
                textBox.CaretIndex = textBox.Text.Length;
                e.Handled = true;
                break;
        }
    }

    #endregion

    #region Dragging an item

    private void OnItemMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: InventoryItemViewModel item } element)
            return;

        _pressedItem = item;
        _pressPoint = e.GetPosition(this);
        _grabOffset = e.GetPosition(element);
    }

    /// <summary>A click (the button went up without a drag): the item inspector opens next to the item.</summary>
    private void OnItemMouseUp(object sender, MouseButtonEventArgs e)
    {
        var clicked = _pressedItem;
        _pressedItem = null;
        if (clicked is null || sender is not FrameworkElement { DataContext: InventoryItemViewModel item } element
            || !ReferenceEquals(clicked, item) || ViewModel is not { CanEditItems: true } viewModel)
            return;

        e.Handled = true;
        // After the mouse input is done, so the inspector takes the focus.
        Dispatcher.InvokeAsync(() => viewModel.EditItem(item, element), DispatcherPriority.Input);
    }

    private void OnItemMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressedItem is not { } item || e.LeftButton != MouseButtonState.Pressed || sender is not FrameworkElement element)
            return;

        var moved = e.GetPosition(this) - _pressPoint;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _pressedItem = null;
        if (ViewModel is not { } viewModel || !viewModel.CanDrag(item))
            return;

        item.IsDragging = true;
        try
        {
            DragDrop.DoDragDrop(element, new DataObject(DragFormat, item), DragDropEffects.Move);
        }
        finally
        {
            item.IsDragging = false;
            viewModel.MainBag.HidePreview();
            foreach (var bag in viewModel.Bags)
                bag.HidePreview();
        }
    }

    private static InventoryItemViewModel? GetDraggedItem(DragEventArgs e) =>
        e.Data.GetDataPresent(DragFormat) ? e.Data.GetData(DragFormat) as InventoryItemViewModel : null;

    /// <summary>The cell the dragged item's top-left corner would snap to.</summary>
    private (int X, int Y) GetTargetCell(IInputElement grid, DragEventArgs e)
    {
        var position = e.GetPosition(grid);
        var size = InventoryItemViewModel.CellSize;
        return ((int)Math.Floor((position.X - _grabOffset.X) / size + 0.5),
                (int)Math.Floor((position.Y - _grabOffset.Y) / size + 0.5));
    }

    #endregion

    #region Dropping on a bag

    private void OnBagDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (GetDraggedItem(e) is not { } item || sender is not FrameworkElement { DataContext: InventoryBagViewModel bag } grid)
            return;

        var (x, y) = GetTargetCell(grid, e);
        bag.ShowPreview(item, x, y);
        if (bag.IsPreviewValid)
            e.Effects = DragDropEffects.Move;
    }

    private void OnBagDragLeave(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: InventoryBagViewModel bag } grid)
            return;

        // DragLeave also comes from the items on the bag; only leaving the bag itself hides the preview.
        var position = e.GetPosition(grid);
        if (position.X < 0 || position.Y < 0 || position.X >= grid.ActualWidth || position.Y >= grid.ActualHeight)
            bag.HidePreview();
    }

    private void OnBagDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (GetDraggedItem(e) is not { } item || sender is not FrameworkElement { DataContext: InventoryBagViewModel bag } grid
            || ViewModel is not { } viewModel)
            return;

        var (x, y) = GetTargetCell(grid, e);
        bag.HidePreview();
        viewModel.MoveItem(item, bag, x, y);
    }

    #endregion

    #region Dropping on a bag's tab button

    /// <summary>Holding an item over a bag's tab shows that bag, so the item can be dropped on one of its cells.</summary>
    private void OnBagTabDragEnter(object sender, DragEventArgs e)
    {
        if (GetDraggedItem(e) is not null && sender is FrameworkElement { DataContext: InventoryBagViewModel bag } && ViewModel is { } viewModel)
            viewModel.SelectedBag = bag;
    }

    private void OnBagTabDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = GetDraggedItem(e) is { } item && sender is FrameworkElement { DataContext: InventoryBagViewModel bag }
                    && ViewModel?.CanDrag(item) == true && bag.FindFreeCell(item) is not null
            ? DragDropEffects.Move
            : DragDropEffects.None;
    }

    /// <summary>An item dropped on a tab goes to the first free cell of that bag.</summary>
    private void OnBagTabDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (GetDraggedItem(e) is { } item && sender is FrameworkElement { DataContext: InventoryBagViewModel bag }
            && ViewModel is { } viewModel && bag.FindFreeCell(item) is { } cell)
            viewModel.MoveItem(item, bag, cell.X, cell.Y);
    }

    #endregion

    #region Adding, changing and removing items

    /// <summary>
    /// Right-click on a bag: "Change…" and "Remove" for the item under the mouse, the "Build a new item" menu on a free cell.
    /// </summary>
    private void OnBagMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is not { CanEditItems: true } viewModel
            || sender is not FrameworkElement { DataContext: InventoryBagViewModel bag } grid)
            return;

        e.Handled = true;
        var menu = new ContextMenu
        {
            PlacementTarget = grid,
            Placement = PlacementMode.MousePoint
        };
        if (TryFindResource("ThemedContextMenuStyle") is Style menuStyle)
            menu.Style = menuStyle;
        var itemStyle = TryFindResource("ThemedMenuItemStyle") as Style;

        if (FindItem(e.OriginalSource as DependencyObject, grid) is { } found)
        {
            var (item, host) = found;
            var edit = new MenuItem { Header = "Change…", Icon = MenuIcon(MaterialIconKind.PencilOutline), Style = itemStyle };
            // After the menu has closed, so the inspector (or the question) takes the focus.
            edit.Click += (_, _) => Dispatcher.InvokeAsync(() => viewModel.EditItem(item, host), DispatcherPriority.Background);
            menu.Items.Add(edit);
            menu.Items.Add(new Separator { Style = TryFindResource("ThemedMenuSeparatorStyle") as Style });

            var remove = new MenuItem { Header = "Remove", Icon = MenuIcon(MaterialIconKind.DeleteOutline), Style = itemStyle };
            remove.Click += (_, _) => Dispatcher.InvokeAsync(() => viewModel.RemoveItem(item), DispatcherPriority.Background);
            menu.Items.Add(remove);
        }
        else
        {
            var position = e.GetPosition(grid);
            AddNewItemEntries(menu, itemStyle, viewModel, bag, InventoryBagViewModel.CellAt(position.X, position.Y), grid);
        }

        menu.IsOpen = true;
    }

    /// <summary>
    /// "Build a new item" menu of a free cell, like the Last Epoch editor's: a title, then the item kinds in groups
    /// (armor, weapons, jewelry and relics, the rest). Picking one opens the item inspector listing only that kind; the
    /// item goes to the clicked cell when it fits there. With every bag full the entries are disabled.
    /// </summary>
    private void AddNewItemEntries(
        ContextMenu menu, Style? itemStyle, InventoryViewModel viewModel, InventoryBagViewModel bag, (int X, int Y) cell, FrameworkElement anchor)
    {
        // Title: a menu item that can be neither highlighted nor clicked.
        menu.Items.Add(new MenuItem
        {
            Header = new TextBlock
            {
                Text = "Build a new item",
                FontWeight = FontWeights.SemiBold,
                Style = TryFindResource("SecondaryTextStyle") as Style
            },
            Style = itemStyle,
            Focusable = false,
            IsHitTestVisible = false
        });

        var hasRoom = viewModel.HasFreeCell;
        foreach (var group in NewItemKinds.MenuGroups)
        {
            // Every group, the first included (it follows the title), starts with a separator.
            menu.Items.Add(new Separator { Style = TryFindResource("ThemedMenuSeparatorStyle") as Style });
            foreach (var kind in group)
            {
                var entry = new MenuItem
                {
                    Header = NewItemKinds.GetName(kind),
                    Icon = MenuIcon(NewItemKinds.GetIconName(kind)),
                    IsEnabled = hasRoom,
                    Style = itemStyle,
                    ToolTip = hasRoom ? null : "Every bag is full: make room first"
                };
                ToolTipService.SetShowOnDisabled(entry, true);

                var chosen = kind;
                // After the menu has closed, so the inspector keeps the focus it takes.
                entry.Click += (_, _) => Dispatcher.InvokeAsync(
                    () => viewModel.AddItem(bag, cell, anchor, chosen),
                    DispatcherPriority.Background);
                menu.Items.Add(entry);
            }
        }
    }

    /// <summary>
    /// The bag item an element belongs to and the element that shows it (the item host carries the item as its data
    /// context), up to the bag grid.
    /// </summary>
    private static (InventoryItemViewModel Item, FrameworkElement Host)? FindItem(DependencyObject? node, DependencyObject grid)
    {
        for (; node is not null && !ReferenceEquals(node, grid);
             node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (node is FrameworkElement { DataContext: InventoryItemViewModel item } host)
                return (item, host);
        }

        return null;
    }

    private static MaterialIcon MenuIcon(MaterialIconKind kind) => new() { Kind = kind, Width = 16, Height = 16 };

    /// <summary>A 16 px icon by its <see cref="MaterialIconKind"/> name; <c>null</c> (no icon) for an unknown name.</summary>
    private static MaterialIcon? MenuIcon(string kind) =>
        Enum.TryParse<MaterialIconKind>(kind, out var iconKind) ? MenuIcon(iconKind) : null;

    #endregion
}
