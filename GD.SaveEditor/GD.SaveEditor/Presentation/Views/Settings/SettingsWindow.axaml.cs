using Avalonia.Controls;
using GD.SaveEditor.Presentation.Services;
using GD.SaveEditor.Presentation.ViewModels.Settings;

namespace GD.SaveEditor.Presentation.Views.Settings;

/// <summary>Settings dialog. It closes with <c>true</c> when the settings were saved.</summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel? _viewModel;

    /// <summary>For the XAML previewer.</summary>
    public SettingsWindow()
    {
        InitializeComponent();
        WindowHelper.UseCustomChrome(this);
    }

    public SettingsWindow(SettingsViewModel viewModel) : this()
    {
        DataContext = _viewModel = viewModel;

        _viewModel.CloseRequested += OnCloseRequested;
        Closed += OnClosed;
    }

    private void OnCloseRequested(object? sender, bool saved)
    {
        if (IsVisible)
            Close(saved);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_viewModel is null)
            return;

        _viewModel.CloseRequested -= OnCloseRequested;
        _viewModel.OnClosed();
    }
}
