using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using GD.SaveEditor.Presentation.ViewModels.Shell;

namespace GD.SaveEditor.Presentation.Views;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        NotificationsPopup.CustomPopupPlacementCallback = PlaceNotificationsPopup;

        // Auto-load characters if the save folder exists (the default one included).
        Loaded += async (_, _) => await viewModel.InitializeAsync();

        // Unsaved changes are lost with the window: ask first.
        Closing += (_, e) => e.Cancel = !viewModel.ConfirmClose();

        StateChanged += (_, _) => UpdateMaximizedLayout();
        SourceInitialized += (_, _) => UpdateMaxSize();
        DpiChanged += (_, _) => UpdateMaxSize();
        SizeChanged += OnSizeChanged;
        // Resource references (not brush instances) so the title follows runtime theme changes.
        Activated += (_, _) => TitleText.SetResourceReference(TextBlock.ForegroundProperty, "WindowForegroundBrush");
        Deactivated += (_, _) => TitleText.SetResourceReference(TextBlock.ForegroundProperty, "TitleBarInactiveForegroundBrush");
    }

    #region Caption buttons

    private void OnMinimizeExecuted(object sender, ExecutedRoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void OnMaximizeExecuted(object sender, ExecutedRoutedEventArgs e) => SystemCommands.MaximizeWindow(this);

    private void OnRestoreExecuted(object sender, ExecutedRoutedEventArgs e) => SystemCommands.RestoreWindow(this);

    private void OnCloseExecuted(object sender, ExecutedRoutedEventArgs e) => SystemCommands.CloseWindow(this);

    // The Maximize caption button is always disabled: the window sizes itself (ResizeMode="NoResize").

    #endregion

    #region Size to content

    /// <summary>
    /// SizeToContent with a WindowChrome window measures the first time with the native frame included, which leaves
    /// a black strip along the right and bottom edges: measure again once the content is on screen.
    /// </summary>
    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        if (SizeToContent != SizeToContent.Manual)
            InvalidateMeasure();
    }

    /// <summary>
    /// The window grows to its content (a character's equipment and bags) but never past the work area of its screen;
    /// above that the content scrolls.
    /// </summary>
    private void UpdateMaxSize()
    {
        var area = GetWorkArea();
        MaxWidth = area.Width;
        MaxHeight = area.Height;
        MinWidth = Math.Min(MinWidth, MaxWidth);
        MinHeight = Math.Min(MinHeight, MaxHeight);
    }

    /// <summary>
    /// Keeps the window centered where it is while it sizes to new content (SizeToContent alone grows it to the right
    /// and down) and inside the work area. It only grows: the size reached becomes its minimum, so the window does not
    /// jump while switching tabs or characters.
    /// </summary>
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded || WindowState != WindowState.Normal || e.PreviousSize.Width <= 0 || e.PreviousSize.Height <= 0)
            return;

        var area = GetWorkArea();
        var left = Left - (e.NewSize.Width - e.PreviousSize.Width) / 2;
        var top = Top - (e.NewSize.Height - e.PreviousSize.Height) / 2;

        Left = Math.Clamp(left, area.Left, Math.Max(area.Left, area.Right - e.NewSize.Width));
        Top = Math.Clamp(top, area.Top, Math.Max(area.Top, area.Bottom - e.NewSize.Height));

        MinWidth = Math.Min(Math.Max(MinWidth, e.NewSize.Width), MaxWidth);
        MinHeight = Math.Min(Math.Max(MinHeight, e.NewSize.Height), MaxHeight);
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    /// <summary>Work area (screen without the taskbar) of the monitor the window is on, in device-independent units.</summary>
    private Rect GetWorkArea()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return SystemParameters.WorkArea;

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), ref info))
            return SystemParameters.WorkArea;

        var dpi = VisualTreeHelper.GetDpi(this);
        return new Rect(
            info.Work.Left / dpi.DpiScaleX,
            info.Work.Top / dpi.DpiScaleY,
            (info.Work.Right - info.Work.Left) / dpi.DpiScaleX,
            (info.Work.Bottom - info.Work.Top) / dpi.DpiScaleY);
    }

    #endregion

    #region Maximized layout

    private const int SM_CXSIZEFRAME = 32;
    private const int SM_CYSIZEFRAME = 33;
    private const int SM_CXPADDEDBORDER = 92;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    /// <summary>
    /// A maximized window is larger than the screen by its (invisible) resize frame on every side. With the native
    /// title bar removed, that frame would cut off the edges of the content: pad it away while maximized, and drop the
    /// 1px window border, as Windows does.
    /// </summary>
    private void UpdateMaximizedLayout()
    {
        if (WindowState == WindowState.Maximized)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var padded = GetSystemMetrics(SM_CXPADDEDBORDER);
            var x = (GetSystemMetrics(SM_CXSIZEFRAME) + padded) / dpi.DpiScaleX;
            var y = (GetSystemMetrics(SM_CYSIZEFRAME) + padded) / dpi.DpiScaleY;

            RootBorder.BorderThickness = new Thickness(0);
            RootBorder.Margin = new Thickness(x, y, x, y);
        }
        else
        {
            RootBorder.BorderThickness = new Thickness(1);
            RootBorder.Margin = new Thickness(0);
        }
    }

    #endregion

    #region Notification popup

    /// <summary>Transparent margin around the popup content that holds its drop shadow (see MainWindow.xaml).</summary>
    private const double PopupShadowMargin = 8;

    /// <summary>Gap between the bell button and the popup.</summary>
    private const double PopupGap = 2;

    /// <summary>
    /// Opens the popup below the bell with its right edge aligned to the bell's right edge,
    /// independent of the system menu-drop alignment. Sizes and offsets here are in device pixels.
    /// </summary>
    private CustomPopupPlacement[] PlaceNotificationsPopup(Size popupSize, Size targetSize, Point offset)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var margin = PopupShadowMargin * dpi.DpiScaleX;
        var x = targetSize.Width - popupSize.Width + margin;
        var y = targetSize.Height + (PopupGap - PopupShadowMargin) * dpi.DpiScaleY;

        return [new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.Vertical)];
    }

    #endregion
}
