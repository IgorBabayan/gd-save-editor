using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GD.SaveEditor.Presentation.Services;
using GD.SaveEditor.Presentation.ViewModels.Shell;

namespace GD.SaveEditor.Presentation.Views;

/// <summary>
/// Main window: the characters of the save folder on the left, the selected one's details on the right. It sizes itself
/// to its content (never past the work area of its screen) and stays centered as it grows.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel? _viewModel;

    /// <summary>Set once the user agreed to close (unsaved changes are lost with the window).</summary>
    private bool _closeConfirmed;

    /// <summary>For the XAML previewer.</summary>
    public MainWindow()
    {
        InitializeComponent();
        WindowHelper.UseCustomChrome(this, CaptionButtons);

        // The window sizes itself to its content and the user cannot resize it. X11 window managers (and macOS) keep a
        // non-resizable window at the size it was first shown with, so there it stays resizable to be able to grow.
        CanResize = !OperatingSystem.IsWindows();
    }

    public MainWindow(MainViewModel viewModel) : this()
    {
        DataContext = _viewModel = viewModel;

        // Auto-load characters if the save folder exists (the default one included).
        Opened += async (_, _) =>
        {
            UpdateMaxSize();
            await viewModel.InitializeAsync();
        };

        Closing += OnClosing;
        Resized += OnResized;
        PositionChanged += (_, _) => UpdateMaxSize();
        ScalingChanged += (_, _) => UpdateMaxSize();

        // Resource references (not brush instances) so the title follows runtime theme changes.
        Activated += (_, _) => TitleText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("WindowForegroundBrush"));
        Deactivated += (_, _) => TitleText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("TitleBarInactiveForegroundBrush"));
    }

    #region Caption buttons

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    // The Maximize caption button is always disabled: the window sizes itself (CanResize="False").

    #endregion

    #region Closing

    /// <summary>
    /// Unsaved changes are lost with the window: ask first. The question is asked after the close request returned
    /// (a dialog must not run inside the platform's close notification); a Yes closes the window again.
    /// </summary>
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || _viewModel is null || e.CloseReason == WindowCloseReason.ApplicationShutdown)
            return;

        e.Cancel = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_viewModel.ConfirmClose())
                return;

            _closeConfirmed = true;
            Close();
        });
    }

    #endregion

    #region Size to content

    /// <summary>
    /// The window grows to its content (a character's equipment and bags) but never past the work area of its screen;
    /// above that the content scrolls.
    /// </summary>
    private void UpdateMaxSize()
    {
        if (GetWorkArea() is not { } area)
            return;

        MaxWidth = area.Width;
        MaxHeight = area.Height;
        MinWidth = Math.Min(MinWidth, MaxWidth);
        MinHeight = Math.Min(MinHeight, MaxHeight);
    }

    /// <summary>A resize by the window manager (X11 places and frames the window after showing it) switches SizeToContent off.</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // The window is not meant to be sized by hand, so it goes back to sizing itself.
        if (change.Property == SizeToContentProperty && change.GetNewValue<SizeToContent>() == SizeToContent.Manual
            && WindowState == WindowState.Normal)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (WindowState != WindowState.Normal)
                    return;

                SizeToContent = SizeToContent.WidthAndHeight;
                // The content was measured for the fixed size meanwhile: measure it again for its own size.
                InvalidateMeasure();
            });
        }
    }

    /// <summary>Size the content gave the window last; <c>null</c> until the first layout.</summary>
    private Size? _layoutSize;

    /// <summary>
    /// Keeps the window centered where it is while it sizes to new content (SizeToContent alone grows it to the right
    /// and down) and inside the work area. It only grows: the size reached becomes its minimum, so the window does not
    /// jump while switching tabs or characters. Only sizes coming from the layout count; what a window manager does
    /// while placing the window does not.
    /// </summary>
    private void OnResized(object? sender, WindowResizedEventArgs e)
    {
        if (e.Reason != WindowResizeReason.Layout || WindowState != WindowState.Normal)
            return;

        var size = e.ClientSize;
        var previous = _layoutSize;
        _layoutSize = size;
        if (previous is not { } old || old.Width <= 0 || old.Height <= 0 || GetWorkArea() is not { } area)
            return;

        var scaling = DesktopScaling;
        var left = Position.X / scaling - (size.Width - old.Width) / 2;
        var top = Position.Y / scaling - (size.Height - old.Height) / 2;
        left = Math.Clamp(left, area.Left, Math.Max(area.Left, area.Right - size.Width));
        top = Math.Clamp(top, area.Top, Math.Max(area.Top, area.Bottom - size.Height));
        Position = new PixelPoint((int)Math.Round(left * scaling), (int)Math.Round(top * scaling));

        MinWidth = Math.Min(Math.Max(MinWidth, size.Width), MaxWidth);
        MinHeight = Math.Min(Math.Max(MinHeight, size.Height), MaxHeight);
    }

    /// <summary>Work area (screen without the taskbar or panels) of the screen the window is on, in device-independent units.</summary>
    private Rect? GetWorkArea()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
            return null;

        // Some X11 setups (no window manager) report no work area: use the whole screen then.
        var work = screen.WorkingArea.Width > 0 && screen.WorkingArea.Height > 0 ? screen.WorkingArea : screen.Bounds;
        if (work.Width <= 0 || work.Height <= 0)
            return null;

        var scaling = screen.Scaling > 0 ? screen.Scaling : 1;
        return new Rect(work.X / scaling, work.Y / scaling, work.Width / scaling, work.Height / scaling);
    }

    #endregion
}
