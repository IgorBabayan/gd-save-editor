using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GD.SaveEditor.Presentation.ViewModels.Inventory;

namespace GD.SaveEditor.Presentation.Views.Inventory;

/// <summary>
/// Bags and iron bits of the selected character, shown under the equipment in the "Character" tab. Items are moved by
/// drag and drop: onto a cell of the main bag or of the shown bag, or onto a bag's tab button (first free cell; holding
/// an item over a tab also shows that bag).
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

    private void OnItemMouseUp(object sender, MouseButtonEventArgs e) => _pressedItem = null;

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
}
