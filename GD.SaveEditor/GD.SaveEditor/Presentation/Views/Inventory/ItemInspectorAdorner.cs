using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GD.SaveEditor.Presentation.ViewModels.Inventory;

namespace GD.SaveEditor.Presentation.Views.Inventory;

/// <summary>
/// Shows an <see cref="ItemInspectorView"/> next to the bag item that was clicked, as a flyout on the window's adorner
/// layer. Living inside the window (not in a popup) keeps it under the window's own dialogs, such as the "Remove item"
/// question, and lets it follow the window's theme.
/// It closes on Apply / Cancel, Esc, a click outside it, or when the item leaves the screen; only Apply changes the bag.
/// One inspector is open at a time.
/// </summary>
internal sealed class ItemInspectorAdorner : Control
{
    /// <summary>Space between the item and the inspector.</summary>
    private const double Gap = 8;

    /// <summary>Minimal distance to the window edges.</summary>
    private const double EdgeMargin = 8;

    private static ItemInspectorAdorner? _current;

    private readonly Control _root;
    private readonly Window _window;
    private readonly Control _anchor;
    private readonly ItemBuilderViewModel _viewModel;
    private readonly ItemInspectorView _view;
    private readonly Action<BuiltItem> _applied;
    private readonly Action<ItemBuilderViewModel> _openFullBuilder;
    private bool _isOnLayer;
    private bool _isClosed;

    private ItemInspectorAdorner(
        Control root,
        Window window,
        Control anchor,
        ItemBuilderViewModel viewModel,
        Action<BuiltItem> applied,
        Action<ItemBuilderViewModel> openFullBuilder)
    {
        _root = root;
        _window = window;
        _anchor = anchor;
        _viewModel = viewModel;
        _applied = applied;
        _openFullBuilder = openFullBuilder;
        _view = new ItemInspectorView(viewModel);
        LogicalChildren.Add(_view);
        VisualChildren.Add(_view);
    }

    /// <summary>
    /// Opens the inspector for <paramref name="viewModel"/> next to <paramref name="anchor"/>, closing any other one.
    /// </summary>
    /// <param name="applied">Called with the built item when the user applies it.</param>
    /// <param name="openFullBuilder">Shows the full item builder window (modal) over the same view model.</param>
    /// <returns><c>false</c> when the anchor is not in a window with an adorner layer (nothing was opened).</returns>
    public static bool TryShow(
        Control anchor,
        ItemBuilderViewModel viewModel,
        Action<BuiltItem> applied,
        Action<ItemBuilderViewModel> openFullBuilder)
    {
        _current?.Close();

        if (TopLevel.GetTopLevel(anchor) is not Window { Content: Control root } window
            || !root.IsVisualAncestorOf(anchor)
            || AdornerLayer.GetAdornerLayer(root) is null)
            return false;

        _current = new ItemInspectorAdorner(root, window, anchor, viewModel, applied, openFullBuilder);
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
        _window.AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel);
        _window.AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        // A scrolled character page moves the item; follow it.
        _window.AddHandler(ScrollViewer.ScrollChangedEvent, OnScrollChanged, RoutingStrategies.Bubble, handledEventsToo: true);
        _window.Closed += OnWindowClosed;
        _anchor.DetachedFromVisualTree += OnAnchorDetached;

        AddToLayer();
    }

    private void Close()
    {
        if (_isClosed)
            return;
        _isClosed = true;

        _viewModel.CloseRequested -= OnCloseRequested;
        _view.ExpandRequested -= OnExpandRequested;
        _window.RemoveHandler(PointerPressedEvent, OnWindowPointerPressed);
        _window.RemoveHandler(KeyDownEvent, OnWindowKeyDown);
        _window.RemoveHandler(ScrollViewer.ScrollChangedEvent, OnScrollChanged);
        _window.Closed -= OnWindowClosed;
        _anchor.DetachedFromVisualTree -= OnAnchorDetached;

        var hadFocus = _view.IsKeyboardFocusWithin;
        RemoveFromLayer();
        if (_current == this)
            _current = null;

        // Hand the keyboard back to the item so the keyboard keeps working from where the user was.
        if (hadFocus && _anchor.IsAttachedToVisualTree())
            _anchor.Focus();
    }

    private void AddToLayer()
    {
        if (_isOnLayer)
            return;

        AdornerLayer.SetAdorner(_root, this);
        _isOnLayer = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_isClosed)
                _view.FocusFirst();
        }, DispatcherPriority.Input);
    }

    private void RemoveFromLayer()
    {
        if (!_isOnLayer)
            return;

        if (ReferenceEquals(AdornerLayer.GetAdorner(_root), this))
            AdornerLayer.SetAdorner(_root, null);
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

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var source = e.Source as Visual;
        if (IsWithin(_view, source) || !_isOnLayer)
            return;

        // A click outside cancels; a click on the same item only closes it (instead of reopening it).
        var onAnchor = IsWithin(_anchor, source);
        Close();
        if (onAnchor)
            e.Handled = true;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !_isOnLayer)
            return;

        // Esc first closes an open part picker, then the inspector.
        if (_viewModel.IsPicking)
            _viewModel.ClosePickerCommand.Execute(null);
        else
            Close();
        e.Handled = true;
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (!IsWithin(_view, e.Source as Visual))
            InvalidateArrange();
    }

    private void OnAnchorDetached(object? sender, VisualTreeAttachmentEventArgs e) => Close();

    private void OnWindowClosed(object? sender, EventArgs e) => Close();

    /// <summary><c>true</c> when <paramref name="source"/> is <paramref name="ancestor"/> or inside it (visual or logical tree).</summary>
    private static bool IsWithin(Visual ancestor, Visual? source)
    {
        if (source is null)
            return false;

        if (ReferenceEquals(source, ancestor) || ancestor.IsVisualAncestorOf(source))
            return true;

        // Popups of the inspector (a tooltip, a drop-down) are only its logical descendants.
        return ancestor is ILogical logical && source is ILogical node && logical.IsLogicalAncestorOf(node);
    }

    // ===== Layout =====

    protected override Size MeasureOverride(Size availableSize)
    {
        var area = _root.Bounds.Size;
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
        var area = _root.Bounds.Size;
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
        if (_anchor.IsAttachedToVisualTree() && _anchor.TranslatePoint(default, _root) is { } topLeft)
            return new Rect(topLeft, _anchor.Bounds.Size);

        var area = _root.Bounds.Size;
        return new Rect(area.Width / 2, area.Height / 4, 0, 0);
    }
}
