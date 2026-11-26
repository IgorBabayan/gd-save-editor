using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace GD.SaveEditor.Presentation.Services;

internal static class WindowHelper
{
    /// <summary>The currently active window, falling back to the main window. Used as dialog owner.</summary>
    public static Window? GetActiveWindow()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;

        return desktop.Windows.FirstOrDefault(w => w.IsActive) ?? desktop.MainWindow;
    }

    /// <summary>
    /// Shows <paramref name="window"/> as a modal dialog of the active window and returns once it closed, with the value
    /// it was closed with (<see cref="Window.Close(object?)"/>), like WPF's <c>ShowDialog</c>. The UI keeps running in a
    /// nested dispatcher frame meanwhile, so callers keep their synchronous flow.
    /// </summary>
    public static TResult? ShowModal<TResult>(Window window)
    {
        var owner = GetActiveWindow();
        if (owner is null || ReferenceEquals(owner, window))
        {
            // No window to be modal to (start-up): show it on its own and wait for it to close.
            var closed = new TaskCompletionSource<TResult?>();
            window.Closed += (_, _) => closed.TrySetResult(default);
            window.Show();
            return Wait(closed.Task);
        }

        return Wait(window.ShowDialog<TResult?>(owner));
    }

    /// <summary>
    /// The windows draw their own title bar over the extended client area. Where the platform cannot extend the client
    /// area (some Linux window managers), <paramref name="window"/> gets the system's full decorations back once it is
    /// open, and the app-drawn caption buttons (<paramref name="captionButtons"/>) are hidden, so the window can always
    /// be moved and closed.
    /// </summary>
    public static void UseCustomChrome(Window window, Control? captionButtons = null)
    {
        window.Opened += (_, _) =>
        {
            if (window.IsExtendedIntoWindowDecorations)
                return;

            window.WindowDecorations = WindowDecorations.Full;
            if (captionButtons is not null)
                captionButtons.IsVisible = false;
        };
    }

    /// <summary>Runs the dispatcher until <paramref name="task"/> completes, then returns its result.</summary>
    public static T Wait<T>(Task<T> task)
    {
        if (!task.IsCompleted && Dispatcher.UIThread.CheckAccess() && Dispatcher.UIThread.SupportsRunLoops)
        {
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => Dispatcher.UIThread.Post(() => frame.Continue = false, DispatcherPriority.Send),
                TaskScheduler.Default);
            Dispatcher.UIThread.PushFrame(frame);
        }

        return task.GetAwaiter().GetResult();
    }
}
