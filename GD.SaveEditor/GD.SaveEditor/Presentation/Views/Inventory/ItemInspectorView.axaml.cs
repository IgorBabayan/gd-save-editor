using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GD.SaveEditor.Presentation.ViewModels.Inventory;

namespace GD.SaveEditor.Presentation.Views.Inventory;

/// <summary>
/// Compact item builder shown next to a bag item (see <see cref="ItemInspectorAdorner"/>): an object inspector over
/// <see cref="ItemBuilderViewModel"/> with expandable rows instead of the full builder window. Which rows are expanded
/// is view state and lives here.
/// </summary>
public partial class ItemInspectorView : UserControl
{
    private readonly ItemBuilderViewModel? _viewModel;

    /// <summary>For the XAML previewer.</summary>
    public ItemInspectorView()
    {
        InitializeComponent();
    }

    public ItemInspectorView(ItemBuilderViewModel viewModel) : this()
    {
        DataContext = _viewModel = viewModel;

        // A new item starts with the item list open; an existing item starts with it closed.
        ItemToggle.IsChecked = !viewModel.CanRemove && viewModel.IsDatabaseAvailable;

        // Pills fold their row away once picked (Click bubbles up from the buttons of the pill templates).
        AddHandler(Button.ClickEvent, OnButtonClick, RoutingStrategies.Bubble);
        // Clicking an item of the list selects it (on press) and folds the list away (on release).
        ItemList.AddHandler(PointerReleasedEvent, OnItemListPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>The "open the full item builder" button was clicked.</summary>
    public event EventHandler? ExpandRequested;

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (_viewModel is null)
            return;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        // Reads the item list once the inspector shows (the full window over the same view model does not read it again).
        try
        {
            await _viewModel.LoadAsync();
        }
        catch (Exception)
        {
            // Async void: LoadAsync reports its problems in the view model; nothing may escape here.
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    /// <summary>Puts the keyboard focus into the inspector: the item search when the list is open.</summary>
    public void FocusFirst()
    {
        if (ItemToggle.IsChecked == true && ItemSearch.IsEffectivelyEnabled && ItemSearch.IsEffectivelyVisible)
            ItemSearch.Focus();
        else
            Focus();
    }

    private void OnExpandClick(object? sender, RoutedEventArgs e) => ExpandRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>The item list was expanded: type to search right away (the search text is selected to replace it).</summary>
    private void OnItemToggleChanged(object? sender, RoutedEventArgs e)
    {
        if (ItemToggle.IsChecked != true)
            return;

        // After the list became visible: a hidden text box cannot take the focus.
        Dispatcher.UIThread.Post(() =>
        {
            if (ItemToggle.IsChecked != true || !ItemSearch.IsEffectivelyVisible || !ItemSearch.IsEffectivelyEnabled)
                return;

            ItemSearch.Focus();
            ItemSearch.SelectAll();
        }, DispatcherPriority.Input);
    }

    /// <summary>A category or rarity was picked: fold the pills away like a closed drop-down and show the list it filters.</summary>
    private void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button button)
            return;

        if (button.Classes.Contains("categoryPill"))
        {
            CategoryToggle.IsChecked = false;
            ItemToggle.IsChecked = true;
        }
        else if (button.Classes.Contains("rarityPill"))
        {
            RarityToggle.IsChecked = false;
            ItemToggle.IsChecked = true;
        }
    }

    /// <summary>Clicking an item selects it (on press) and folds the list away.</summary>
    private void OnItemListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left)
            return;

        for (var node = e.Source as Visual; node is not null && !ReferenceEquals(node, ItemList); node = node.GetVisualParent())
        {
            if (node is ListBoxItem { IsEnabled: true })
            {
                CollapseItemList();
                return;
            }
        }
    }

    private void OnItemListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ItemList.SelectedItem is not null)
        {
            CollapseItemList();
            e.Handled = true;
        }
    }

    private void CollapseItemList()
    {
        // After the click is done, so the list is not hidden under the pointer mid-gesture.
        Dispatcher.UIThread.Post(() =>
        {
            if (_viewModel?.HasSelection == true)
                ItemToggle.IsChecked = false;
        }, DispatcherPriority.Input);
    }

    /// <summary>The part picker opened under a part: type to filter right away.</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ItemBuilderViewModel.IsPicking) || _viewModel?.IsPicking != true)
            return;

        // After the picker's template was applied.
        Dispatcher.UIThread.Post(() =>
        {
            this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.Classes.Contains("pickerSearch"))?.Focus();
        }, DispatcherPriority.Loaded);
    }
}
