using System.Windows;

namespace GD.SaveEditor.Presentation.Services;

internal static class WindowHelper
{
    /// <summary>The currently active window, falling back to the main window. Used as dialog owner.</summary>
    public static Window? GetActiveWindow()
    {
        var app = Application.Current;
        if (app is null)
            return null;

        return app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? app.MainWindow;
    }
}
