using System.Windows;
using GD.SaveEditor.Presentation.ViewModels.Settings;

namespace GD.SaveEditor.Presentation.Views.Settings;

/// <summary>
/// Interaction logic for SettingsWindow.xaml
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;

        _viewModel.CloseRequested += OnCloseRequested;
        Closed += OnClosed;
    }

    private void OnCloseRequested(object? sender, bool saved)
    {
        // Setting DialogResult on a modal window closes it.
        if (IsVisible)
            DialogResult = saved;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.CloseRequested -= OnCloseRequested;
        _viewModel.OnClosed();
    }
}
