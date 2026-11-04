using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using GD.SaveEditor.Presentation.Services.Toasts;

namespace GD.SaveEditor.Presentation.Views.Toasts;

/// <summary>
/// Overlay that shows the toasts of <see cref="IToastService"/>.
/// Every window that can be in front hosts one; a host hides itself while one of its window's
/// owned dialogs (e.g. Settings) is open, so toasts appear only once, in the frontmost window.
/// </summary>
public partial class ToastHost : UserControl
{
    private Window? _window;

    public ToastHost()
    {
        InitializeComponent();

        if (DesignerProperties.GetIsInDesignMode(this))
            return;

        // Created by XAML, so constructor injection is not available here.
        DataContext = App.Services.GetRequiredService<IToastService>();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _window = Window.GetWindow(this);
        if (_window is null)
            return;

        _window.Activated += OnWindowActivationChanged;
        _window.Deactivated += OnWindowActivationChanged;
        UpdateVisibility();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_window is null)
            return;

        _window.Activated -= OnWindowActivationChanged;
        _window.Deactivated -= OnWindowActivationChanged;
        _window = null;
    }

    private void OnWindowActivationChanged(object? sender, EventArgs e) => UpdateVisibility();

    private void UpdateVisibility()
    {
        if (_window is null)
            return;

        // An open owned dialog hosts the toasts itself (it is in front of this window).
        var hasOpenDialog = !_window.IsActive && _window.OwnedWindows.OfType<Window>().Any(w => w.IsVisible);
        Visibility = hasOpenDialog ? Visibility.Collapsed : Visibility.Visible;
    }
}
