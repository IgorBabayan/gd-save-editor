using Avalonia;

namespace GD.SaveEditor;

internal static class Program
{
    /// <summary>
    /// Entry point. Nothing here may touch Avalonia before <see cref="AppBuilder"/> is set up: the platform (Win32, X11,
    /// macOS) is picked by <see cref="AppBuilderDesktopExtensions.UsePlatformDetect"/>.
    /// </summary>
    [STAThread]
    public static int Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>Used by <see cref="Main"/> and by the XAML previewer of the IDEs.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
