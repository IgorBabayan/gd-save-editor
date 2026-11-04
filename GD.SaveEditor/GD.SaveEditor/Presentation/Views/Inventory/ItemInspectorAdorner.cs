using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GD.SaveEditor.Presentation.ViewModels.Inventory;

namespace GD.SaveEditor.Presentation.Views.Inventory;

/// <summary>
/// Shows an <see cref="ItemInspectorView"/> next to the bag item that was clicked, as a flyout on the
/// window's adorner layer. Living inside the window (not in a Popup) keeps it under the window's own dialogs,
/// such as the "Remove item" question, and lets it follow the window's theme.
/// It closes on Apply / Cancel, Esc, a click outside it, or when the item leaves the screen; only Apply changes the bag.
/// One inspector is open at a time.
/// </summary>
internal sealed class ItemInspectorAdorner : Adorner
{
    /// <summary>Space between the item and the inspector.</summary>
    private const double Gap = 8;

    /// <summary>Minimal distance to the window edges.</summary>
    private const double EdgeMargin = 8;

    private static ItemInspectorAdorner? _current;

    private readonly AdornerLayer _layer;
    private readonly Window _window;
    private readonly FrameworkElement _anchor;
    private readonly ItemBuilderViewModel _viewModel;
    private readonly ItemInspectorView _view;
    private readonly Action<BuiltItem> _applied;
    private readonly Action<ItemBuilderViewModel> _openFullBuilder;
    private bool _isOnLayer;
    private bool _isClosed;

    private ItemInspectorAdorner(
        UIElement root,
        AdornerLayer layer,
        Window window,
        FrameworkElement anchor,
        ItemBuilderViewModel viewModel,
        Action<BuiltItem> applied,
        Action<ItemBuilderViewModel> openFullBuilder)
        : base(root)
    {
        _layer = layer;
        _window = window;
        _anchor = anchor;
        _viewModel = viewModel;
        _applied = applied;
        _openFullBuilder = openFullBuilder;
        _view = new ItemInspectorView(viewModel);
        AddVisualChild(_view);
    }

    /// <summary>
    /// Opens the inspector for <paramref name="viewModel"/> next to <paramref name="anchor"/>, closing any other one.
    /// </summary>
    /// <param name="applied">Called with the built item when the user applies it.</param>
    /// <param name="openFullBuilder">Shows the full item builder window (modal) over the same view model.</param>
    /// <returns><c>false</c> when the anchor is not in a window with an adorner layer (nothing was opened).</returns>
    public static bool TryShow(
        FrameworkElement anchor,
        ItemBuilderViewModel viewModel,
        Action<BuiltItem> applied,
        Action<ItemBuilderViewModel> openFullBuilder)
    {
        _current?.Close();

        if (Window.GetWindow(anchor) is not { Content: UIElement root } window
            || !root.IsAncestorOf(anchor)
            || AdornerLayer.GetAdornerLayer(root) is not { } layer)
            return false;

        _current = new ItemInspectorAdorner(root, layer, window, anchor, viewModel, applied, openFullBuilder);
        _current.Open();
        return true;
    }

    /// <summary>Closes the open inspector without applying it, if there is one.</summary>
    public static void CloseCurrent() => _current?.Close();

    // ===== Lifetime =====

    private void Open()
    {
        _viewModel.CloseRequested += OnCloseRequested;
        _view.ExpandRequested += OnExpandRequested;
        _window.PreviewMouseDown += OnWindowPreviewMouseDown;
        _window.PreviewKeyDown += OnWindowPreviewKeyDown;
        _window.Closed += OnWindowClosed;
        // A scrolled character page moves the item; follow it.
        _window.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged), true);
        _anchor.Unloaded += OnAnchorUnloaded;

        AddToLayer();
    }

    private void Close()
    {
        if (_isClosed)
            return;
        _isClosed = true;

        _viewModel.CloseRequested -= OnCloseRequested;
        _view.ExpandRequested -= OnExpandRequested;
        _window.PreviewMouseDown -= OnWindowPreviewMouseDown;
        _window.PreviewKeyDown -= OnWindowPreviewKeyDown;
        _window.Closed -= OnWindowClosed;
        _window.RemoveHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
        _anchor.Unloaded -= OnAnchorUnloaded;

        var hadFocus = _view.IsKeyboardFocusWithin;
        RemoveFromLayer();
        if (_current == this)
            _current = null;

        // Hand the keyboard back to the item so the keyboard keeps working from where the user was.
        if (hadFocus && _anchor.IsLoaded)
            _anchor.Focus();
    }

    private void AddToLayer()
    {
        if (_isOnLayer)
            return;
        _layer.Add(this);
        _isOnLayer = true;
        Dispatcher.InvokeAsync(() =>
        {
            if (!_isClosed)
                _view.FocusFirst();
        }, DispatcherPriority.Input);
    }

    private void RemoveFromLayer()
    {
        if (!_isOnLayer)
            return;
        _layer.Remove(this);
        _isOnLayer = false;
    }

    // ===== Events =====

    /// <summary>Apply (with the item) or Cancel (<c>null</c>), from the inspector or the full builder window.</summary>
    private void OnCloseRequested(object? sender, BuiltItem? item)
    {
        if (item is not null)
            _applied(item);
        Close();
    }

    /// <summary>
    /// Swaps the inspector for the full builder window over the same view model. Apply / Cancel there close both;
    /// closing the window any other way brings the inspector back with everything picked so far.
    /// </summary>
    private void OnExpandRequested(object? sender, EventArgs e)
    {
        RemoveFromLayer();
        _openFullBuilder(_viewModel);
        if (!_isClosed)
            AddToLayer();
    }

    private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsWithin(_view, e.OriginalSource))
            return;

        // A click outside cancels; a click on the same item only closes it (instead of reopening it).
        var onAnchor = IsWithin(_anchor, e.OriginalSource);
        Close();
        if (onAnchor)
            e.Handled = true;
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        // Esc first closes an open part picker, then the inspector.
        if (_viewModel.IsPicking)
            _viewModel.ClosePickerCommand.Execute(null);
        else
            Close();
        e.Handled = true;
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!IsWithin(_view, e.OriginalSource))
            InvalidateArrange();
    }

    private void OnAnchorUnloaded(object sender, RoutedEventArgs e) => Close();

    private void OnWindowClosed(object? sender, EventArgs e) => Close();

    /// <summary><c>true</c> when <paramref name="source"/> is <paramref name="ancestor"/> or inside it.</summary>
    private static bool IsWithin(DependencyObject ancestor, object? source)
    {
        for (var node = source as DependencyObject; node is not null; node = GetParent(node))
        {
            if (ReferenceEquals(node, ancestor))
                return true;
        }

        return false;
    }

    private static DependencyObject? GetParent(DependencyObject node) =>
        node is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node)
            : LogicalTreeHelper.GetParent(node);

    // ===== Visual child and layout =====

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) =>
        index == 0 ? _view : throw new ArgumentOutOfRangeException(nameof(index));

    protected override Size MeasureOverride(Size constraint)
    {
        var area = AdornedElement.RenderSize;
        // Never taller than the window: the inspector scrolls its properties instead.
        var maxHeight = Math.Max(160, area.Height - 2 * EdgeMargin);
        if (!_view.MaxHeight.Equals(maxHeight))
            _view.MaxHeight = maxHeight;

        _view.Measure(new Size(double.PositiveInfinity, maxHeight));
        return area;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = _view.DesiredSize;
        var area = AdornedElement.RenderSize;
        var anchor = GetAnchorBounds();

        // Right of the item when it fits, else left of it, else as far right as possible.
        var x = anchor.Right + Gap;
        if (x + size.Width > area.Width - EdgeMargin)
        {
            var left = anchor.Left - Gap - size.Width;
            x = left >= EdgeMargin ? left : area.Width - EdgeMargin - size.Width;
        }

        // Top aligned with the item, pushed up when it would leave the window.
        var y = Math.Min(anchor.Top, area.Height - EdgeMargin - size.Height);

        x = Math.Max(EdgeMargin, x);
        y = Math.Max(EdgeMargin, y);
        _view.Arrange(new Rect(new Point(x, y), size));
        return finalSize;
    }

    /// <summary>Bounds of the item in the adorned root's coordinates (the root's middle if it cannot be found).</summary>
    private Rect GetAnchorBounds()
    {
        if (_anchor.IsLoaded && AdornedElement.IsAncestorOf(_anchor))
            return _anchor.TransformToAncestor(AdornedElement).TransformBounds(new Rect(_anchor.RenderSize));

        var area = AdornedElement.RenderSize;
        return new Rect(area.Width / 2, area.Height / 4, 0, 0);
    }
}
