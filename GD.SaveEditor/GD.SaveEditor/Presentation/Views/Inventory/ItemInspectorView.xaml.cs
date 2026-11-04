using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using GD.SaveEditor.Presentation.ViewModels.Inventory;

namespace GD.SaveEditor.Presentation.Views.Inventory;

/// <summary>
/// Compact item builder shown next to a bag item (see <see cref="ItemInspectorAdorner"/>): an object inspector over
/// <see cref="ItemBuilderViewModel"/> with expandable rows instead of the full builder window. Which rows are expanded
/// is view state and lives here.
/// </summary>
public partial class ItemInspectorView : UserControl
{
    private readonly ItemBuilderViewModel _viewModel;

    public ItemInspectorView(ItemBuilderViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;

        // A new item starts with the item list open; an existing item starts with it closed.
        ItemToggle.IsChecked = !viewModel.CanRemove && viewModel.IsDatabaseAvailable;

        // Reads the item list once the inspector shows (the full window over the same view model does not read it again).
        Loaded += async (_, _) => await _viewModel.LoadAsync();
    }

    /// <summary>The "open the full item builder" button was clicked.</summary>
    public event EventHandler? ExpandRequested;

    /// <summary>Puts the keyboard focus into the inspector: the item search when the list is open.</summary>
    public void FocusFirst()
    {
        if (ItemToggle.IsChecked == true && ItemSearch.IsEnabled)
            ItemSearch.Focus();
        else
            Focus();
    }

    private void OnExpandClick(object sender, RoutedEventArgs e) => ExpandRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>The item list was expanded: type to search right away (the search text is selected to replace it).</summary>
    private void OnItemToggleChecked(object sender, RoutedEventArgs e)
    {
        // After the list became visible: a collapsed text box cannot take the focus.
        Dispatcher.InvokeAsync(() =>
        {
            if (ItemToggle.IsChecked != true || !ItemSearch.IsVisible || !ItemSearch.IsEnabled)
                return;

            ItemSearch.Focus();
            Keyboard.Focus(ItemSearch);
            ItemSearch.SelectAll();
        }, DispatcherPriority.Input);
    }

    /// <summary>A category was picked: fold the pills away like a closed drop-down and show the list it filters.</summary>
    private void OnCategoryPillClick(object sender, RoutedEventArgs e)
    {
        CategoryToggle.IsChecked = false;
        ItemToggle.IsChecked = true;
    }

    /// <summary>A rarity was picked: fold the pills away like a closed drop-down and show the list it filters.</summary>
    private void OnRarityPillClick(object sender, RoutedEventArgs e)
    {
        RarityToggle.IsChecked = false;
        ItemToggle.IsChecked = true;
    }

    /// <summary>Clicking an item selects it (on mouse down) and folds the list away.</summary>
    private void OnItemListPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(ItemList, source) is ListBoxItem)
            CollapseItemList();
    }

    private void OnItemListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ItemList.SelectedItem is not null)
        {
            CollapseItemList();
            e.Handled = true;
        }
    }

    private void CollapseItemList()
    {
        // After the click is done, so the list is not hidden under the mouse mid-gesture.
        Dispatcher.InvokeAsync(() =>
        {
            if (_viewModel.HasSelection)
                ItemToggle.IsChecked = false;
        }, DispatcherPriority.Input);
    }

    /// <summary>The part picker opened under a part: type to filter right away.</summary>
    private void OnPickerSearchLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
            Dispatcher.InvokeAsync(() => box.Focus(), DispatcherPriority.Input);
    }
}
