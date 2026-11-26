using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GD.SaveEditor.Presentation.ViewModels.Inventory;
using GD.SaveEditor.Services.GameData.Items;
using Material.Icons;
using Material.Icons.Avalonia;

namespace GD.SaveEditor.Presentation.Views.Inventory;

/// <summary>
/// Base of the views that show item grids (<see cref="InventoryView"/>, <see cref="Stash.StashGroupView"/>), with their
/// <see cref="ItemGridViewModel"/> as data context. Items are moved by dragging them: onto a cell of a shown grid, or onto
/// a tab button (first free cell; holding an item over a tab also shows that container). A click on an item opens the
/// item inspector next to it (the compact item builder; its expand button opens the full one). A right-click on a free
/// cell offers to build a new item of a kind (helm, weapon, ring, relic, ...) at that cell; on an item it offers to
/// change or remove it. An item can only be dropped on the grids of the view model it belongs to.
/// </summary>
/// <remarks>
/// The view's templates mark their parts with style classes: <c>bagGrid</c> (the cell grid of a bag, data context
/// <see cref="InventoryBagViewModel"/>), <c>bagItem</c> (an item on it, <see cref="InventoryItemViewModel"/>) and
/// <c>bagTab</c> (a tab button, <see cref="InventoryBagViewModel"/>). The drag runs inside the window with the pointer
/// captured (no system drag and drop): it never leaves the view, so it needs no data transfer and works the same on every
/// platform.
/// </remarks>
public class ItemGridView : UserControl
{
    /// <summary>The pointer has to move this far (in pixels) before a press on an item becomes a drag.</summary>
    private const double DragThreshold = 4;

    /// <summary>Item under the pointer since the left button went down on it; dragging starts once the pointer moves.</summary>
    private InventoryItemViewModel? _pressedItem;

    private Control? _pressedElement;
    private Point _pressPoint;

    /// <summary>Where the item was grabbed, from its top-left corner: the item keeps that point under the pointer.</summary>
    private Point _grabOffset;

    /// <summary>The item being dragged; <c>null</c> while no drag is running.</summary>
    private InventoryItemViewModel? _draggedItem;

    /// <summary>Bag showing the drop preview, and the tab the pointer is over (a tab is selected once on entering).</summary>
    private InventoryBagViewModel? _previewBag;
    private Control? _hoveredTab;

    /// <summary>Shown while the item cannot be dropped where the pointer is (created once, not on every move).</summary>
    private Cursor? _noDropCursor;

    private Cursor NoDropCursor => _noDropCursor ??= new Cursor(StandardCursorType.No);

    public ItemGridView()
    {
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);
        AddHandler(PointerCaptureLostEvent, OnPointerCaptureLost, RoutingStrategies.Bubble);
    }

    private ItemGridViewModel? GridViewModel => DataContext as ItemGridViewModel;

    #region Pressing, clicking and dragging an item

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressedItem = null;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || FindMarked(e.Source as Visual, "bagItem") is not { DataContext: InventoryItemViewModel item } element)
            return;

        _pressedItem = item;
        _pressedElement = element;
        _pressPoint = e.GetPosition(this);
        _grabOffset = e.GetPosition(element);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggedItem is not null)
        {
            UpdateDropTarget(e);
            e.Handled = true;
            return;
        }

        if (_pressedItem is not { } item || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var moved = e.GetPosition(this) - _pressPoint;
        if (Math.Abs(moved.X) < DragThreshold && Math.Abs(moved.Y) < DragThreshold)
            return;

        _pressedItem = null;
        if (GridViewModel is not { } viewModel || !viewModel.CanDrag(item))
            return;

        // The drag starts: the pointer stays with this view until the button goes up.
        _draggedItem = item;
        item.IsDragging = true;
        e.Pointer.Capture(this);
        UpdateDropTarget(e);
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Right)
        {
            OnRightClick(e);
            return;
        }

        if (_draggedItem is { } dragged)
        {
            e.Handled = true;
            Drop(dragged, e);
            EndDrag(e.Pointer);
            return;
        }

        // A click (the button went up without a drag): the item inspector opens next to the item.
        var clicked = _pressedItem;
        var clickedElement = _pressedElement;
        _pressedItem = null;
        _pressedElement = null;
        if (clicked is null || clickedElement is null
            || FindMarked(e.Source as Visual, "bagItem") is not { DataContext: InventoryItemViewModel item }
            || !ReferenceEquals(clicked, item) || GridViewModel is not { CanEditItems: true } viewModel)
            return;

        e.Handled = true;
        // After the pointer input is done, so the inspector takes the focus.
        Dispatcher.UIThread.Post(() => viewModel.EditItem(item, clickedElement), DispatcherPriority.Input);
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        // The capture went away without a release over the view (e.g. the window lost the focus): cancel the drag.
        if (_draggedItem is not null)
            EndDrag(null);
    }

    private void EndDrag(IPointer? pointer)
    {
        if (_draggedItem is { } item)
            item.IsDragging = false;
        _draggedItem = null;
        _hoveredTab = null;
        Cursor = null;
        _previewBag = null;

        if (GridViewModel is { } viewModel)
        {
            foreach (var bag in viewModel.Containers)
                bag.HidePreview();
        }

        if (pointer?.Captured == this)
            pointer.Capture(null);
    }

    #endregion

    #region Drop targets: a bag's grid or a bag's tab button

    /// <summary>The grid or the tab under the pointer: the grid shows where the item would go, a tab shows its bag.</summary>
    private void UpdateDropTarget(PointerEventArgs e)
    {
        if (_draggedItem is not { } item || GridViewModel is not { } viewModel)
            return;

        var (grid, tab) = FindTargets(e);

        // Holding an item over a tab shows that bag (or stash tab), so the item can be dropped on one of its cells.
        if (!ReferenceEquals(tab, _hoveredTab))
        {
            _hoveredTab = tab;
            if (tab is { DataContext: InventoryBagViewModel tabBag })
                viewModel.SelectContainer(tabBag);
        }

        if (grid is { DataContext: InventoryBagViewModel bag })
        {
            if (!ReferenceEquals(bag, _previewBag))
                _previewBag?.HidePreview();
            _previewBag = bag;

            var (x, y) = GetTargetCell(grid, e);
            bag.ShowPreview(item, x, y);
            Cursor = bag.IsPreviewValid ? null : NoDropCursor;
            return;
        }

        _previewBag?.HidePreview();
        _previewBag = null;
        Cursor = tab is { DataContext: InventoryBagViewModel target } && target.FindFreeCell(item) is not null
            ? null
            : NoDropCursor;
    }

    private void Drop(InventoryItemViewModel item, PointerEventArgs e)
    {
        Cursor = null;
        if (GridViewModel is not { } viewModel || !viewModel.CanDrag(item))
            return;

        var (grid, tab) = FindTargets(e);
        if (grid is { DataContext: InventoryBagViewModel bag })
        {
            var (x, y) = GetTargetCell(grid, e);
            bag.HidePreview();
            viewModel.MoveItem(item, bag, x, y);
        }
        else if (tab is { DataContext: InventoryBagViewModel tabBag } && tabBag.FindFreeCell(item) is { } cell)
        {
            // An item dropped on a tab goes to the first free cell of that bag.
            viewModel.MoveItem(item, tabBag, cell.X, cell.Y);
        }
    }

    /// <summary>The bag grid and the (enabled) tab button under the pointer, if any.</summary>
    private (Control? Grid, Control? Tab) FindTargets(PointerEventArgs e)
    {
        var hit = this.InputHitTest(e.GetPosition(this)) as Visual;
        var grid = FindMarked(hit, "bagGrid");
        var tab = grid is null ? FindMarked(hit, "bagTab") : null;
        return (grid, tab is { IsEffectivelyEnabled: true } ? tab : null);
    }

    /// <summary>The cell the dragged item's top-left corner would snap to.</summary>
    private (int X, int Y) GetTargetCell(Visual grid, PointerEventArgs e)
    {
        var position = e.GetPosition(grid);
        var size = InventoryItemViewModel.CellSize;
        return ((int)Math.Floor((position.X - _grabOffset.X) / size + 0.5),
                (int)Math.Floor((position.Y - _grabOffset.Y) / size + 0.5));
    }

    /// <summary><paramref name="node"/> or its first visual ancestor (inside this view) with the style class.</summary>
    private Control? FindMarked(Visual? node, string styleClass)
    {
        for (; node is not null && !ReferenceEquals(node, this); node = node.GetVisualParent())
        {
            if (node is Control control && control.Classes.Contains(styleClass))
                return control;
        }

        return null;
    }

    #endregion

    #region Adding, changing and removing items

    /// <summary>
    /// Right-click on a bag: "Change…" and "Remove" for the item under the pointer, the "Build a new item" menu on a free
    /// cell.
    /// </summary>
    private void OnRightClick(PointerReleasedEventArgs e)
    {
        var source = e.Source as Visual;
        if (_draggedItem is not null || GridViewModel is not { CanEditItems: true } viewModel
            || FindMarked(source, "bagGrid") is not { DataContext: InventoryBagViewModel bag } grid)
            return;

        e.Handled = true;
        var menu = new ContextMenu
        {
            Placement = PlacementMode.Pointer,
            Theme = FindTheme("ThemedContextMenuStyle")
        };
        var itemTheme = FindTheme("ThemedMenuItemStyle");

        if (FindMarked(source, "bagItem") is { DataContext: InventoryItemViewModel item } host)
        {
            var edit = new MenuItem { Header = "Change…", Icon = MenuIcon(MaterialIconKind.PencilOutline), Theme = itemTheme };
            // After the menu has closed, so the inspector (or the question) takes the focus.
            edit.Click += (_, _) => Dispatcher.UIThread.Post(() => viewModel.EditItem(item, host), DispatcherPriority.Background);
            menu.Items.Add(edit);
            menu.Items.Add(new Separator { Theme = FindTheme("ThemedMenuSeparatorStyle") });

            var remove = new MenuItem { Header = "Remove", Icon = MenuIcon(MaterialIconKind.DeleteOutline), Theme = itemTheme };
            remove.Click += (_, _) => Dispatcher.UIThread.Post(() => viewModel.RemoveItem(item), DispatcherPriority.Background);
            menu.Items.Add(remove);
        }
        else
        {
            var position = e.GetPosition(grid);
            AddNewItemEntries(menu, itemTheme, viewModel, bag, InventoryBagViewModel.CellAt(position.X, position.Y), grid);
        }

        menu.Open(grid);
    }

    /// <summary>
    /// "Build a new item" menu of a free cell, like the Last Epoch editor's: a title, then the item kinds in groups
    /// (armor, weapons, jewelry and relics, the rest). Picking one opens the item inspector listing only that kind; the
    /// item goes to the clicked cell when it fits there. With every bag full the entries are disabled.
    /// </summary>
    private void AddNewItemEntries(
        ContextMenu menu, Avalonia.Styling.ControlTheme? itemTheme, ItemGridViewModel viewModel, InventoryBagViewModel bag,
        (int X, int Y) cell, Control anchor)
    {
        // Title: a menu item that can be neither highlighted nor clicked.
        var title = new MenuItem
        {
            Header = new TextBlock
            {
                Text = "Build a new item",
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                Theme = FindTheme("SecondaryTextStyle")
            },
            Theme = itemTheme
        };
        title.Classes.Add("title");
        menu.Items.Add(title);

        var hasRoom = viewModel.HasFreeCell;
        foreach (var group in NewItemKinds.MenuGroups)
        {
            // Every group, the first included (it follows the title), starts with a separator.
            menu.Items.Add(new Separator { Theme = FindTheme("ThemedMenuSeparatorStyle") });
            foreach (var kind in group)
            {
                var entry = new MenuItem
                {
                    Header = NewItemKinds.GetName(kind),
                    Icon = MenuIcon(NewItemKinds.GetIconName(kind)),
                    IsEnabled = hasRoom,
                    Theme = itemTheme
                };
                if (!hasRoom)
                {
                    ToolTip.SetTip(entry, "Every bag is full: make room first");
                    ToolTip.SetShowOnDisabled(entry, true);
                }

                var chosen = kind;
                // After the menu has closed, so the inspector keeps the focus it takes.
                entry.Click += (_, _) => Dispatcher.UIThread.Post(
                    () => viewModel.AddItem(bag, cell, anchor, chosen),
                    DispatcherPriority.Background);
                menu.Items.Add(entry);
            }
        }
    }

    private Avalonia.Styling.ControlTheme? FindTheme(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) ? value as Avalonia.Styling.ControlTheme : null;

    private static MaterialIcon MenuIcon(MaterialIconKind kind) => new() { Kind = kind, Width = 16, Height = 16 };

    /// <summary>A 16 px icon by its <see cref="MaterialIconKind"/> name; <c>null</c> (no icon) for an unknown name.</summary>
    private static MaterialIcon? MenuIcon(string kind) =>
        Enum.TryParse<MaterialIconKind>(kind, out var iconKind) ? MenuIcon(iconKind) : null;

    #endregion
}
