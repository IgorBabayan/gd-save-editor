using System.Windows;
using GD.SaveEditor.Extensions;
using GD.SaveEditor.Presentation.Views;
using GD.SaveEditor.Services.Settings;
using GD.SaveEditor.Services.Theming;

namespace GD.SaveEditor;

/// <summary>
/// Interaction logic for App.xaml.
/// Composition root: builds the DI container and creates the main window from it.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    /// <summary>Root service provider. Prefer constructor injection; use this only where WPF creates objects for you.</summary>
    public static IServiceProvider Services =>
        ((App)Current)._services ?? throw new InvalidOperationException("Services are not initialized yet.");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _services = new ServiceCollection()
            .AddApplicationServices()
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

        // Apply the saved theme before any window is created.
        var settings = _services.GetRequiredService<ISettingsService>().Current;
        _services.GetRequiredService<IThemeService>().Apply(settings.Theme);

        var mainWindow = _services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
