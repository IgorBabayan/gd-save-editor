using Avalonia.Controls;
using GD.SaveEditor.Presentation.Services;
using GD.SaveEditor.Presentation.ViewModels.Database;
using GD.SaveEditor.Services.GameData;

namespace GD.SaveEditor.Presentation.Views.Database;

/// <summary>
/// Progress of the database generator. Starts it when shown and refuses to close
/// (Alt+F4 included) until it has finished; then OK closes it.
/// </summary>
public partial class DatabaseGeneratorWindow : Window
{
    private readonly DatabaseGeneratorViewModel? _viewModel;

    /// <summary>For the XAML previewer.</summary>
    public DatabaseGeneratorWindow()
    {
        InitializeComponent();
        WindowHelper.UseCustomChrome(this);
    }

    public DatabaseGeneratorWindow(DatabaseGeneratorViewModel viewModel) : this()
    {
        DataContext = _viewModel = viewModel;

        _viewModel.CloseRequested += OnCloseRequested;
        Opened += OnOpened;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    /// <summary>Outcome of the run; <c>null</c> if the window was closed before the generator finished.</summary>
    public DatabaseGenerationResult? Result => _viewModel?.Result;

    private async void OnOpened(object? sender, EventArgs e)
    {
        // StartAsync never throws: every problem ends up in the view model's result.
        if (_viewModel is not null)
            await _viewModel.StartAsync();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_viewModel?.IsRunning == true)
            e.Cancel = true;
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        if (IsVisible)
            Close(true);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.CloseRequested -= OnCloseRequested;
        Opened -= OnOpened;
    }
}
