using System.Windows;
using GD.SaveEditor.Presentation.ViewModels.Inventory;

namespace GD.SaveEditor.Presentation.Views.Inventory;

/// <summary>
/// Item builder of a bag item (a new one, or one to change). <see cref="Result"/> is the built item, or <c>null</c>
/// when cancelled or removed (<see cref="ItemBuilderViewModel.RemoveRequested"/>).
/// </summary>
public partial class ItemBuilderWindow : Window
{
    private readonly ItemBuilderViewModel _viewModel;

    public ItemBuilderWindow(ItemBuilderViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;

        _viewModel.CloseRequested += OnCloseRequested;
        Closed += (_, _) => _viewModel.CloseRequested -= OnCloseRequested;

        // The item list is read once the window shows, so it opens at once with a "Reading…" note.
        ContentRendered += async (_, _) => await _viewModel.LoadAsync();
    }

    public BuiltItem? Result { get; private set; }

    private void OnCloseRequested(object? sender, BuiltItem? item)
    {
        Result = item;
        // Setting DialogResult on a modal window closes it.
        if (IsVisible)
            DialogResult = item is not null;
    }
}
