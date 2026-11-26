using Avalonia.Controls;
using GD.SaveEditor.Presentation.Services;
using GD.SaveEditor.Presentation.ViewModels.Inventory;

namespace GD.SaveEditor.Presentation.Views.Inventory;

/// <summary>
/// Item builder of a bag item (a new one, or one to change). <see cref="Result"/> is the built item, or <c>null</c>
/// when cancelled or removed (<see cref="ItemBuilderViewModel.RemoveRequested"/>).
/// </summary>
public partial class ItemBuilderWindow : Window
{
    private readonly ItemBuilderViewModel? _viewModel;

    /// <summary>For the XAML previewer.</summary>
    public ItemBuilderWindow()
    {
        InitializeComponent();
        WindowHelper.UseCustomChrome(this);
    }

    public ItemBuilderWindow(ItemBuilderViewModel viewModel) : this()
    {
        DataContext = _viewModel = viewModel;

        _viewModel.CloseRequested += OnCloseRequested;
        Closed += (_, _) => _viewModel.CloseRequested -= OnCloseRequested;

        // The item list is read once the window shows, so it opens at once with a "Reading…" note.
        Opened += async (_, _) =>
        {
            try
            {
                await _viewModel.LoadAsync();
            }
            catch (Exception)
            {
                // Async void: LoadAsync reports its problems in the view model; nothing may escape here.
            }
        };
    }

    public BuiltItem? Result { get; private set; }

    private void OnCloseRequested(object? sender, BuiltItem? item)
    {
        Result = item;
        if (IsVisible)
            Close(item is not null);
    }
}
