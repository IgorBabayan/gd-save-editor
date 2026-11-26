using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GD.SaveEditor.Extensions;
using GD.SaveEditor.Presentation.Services;
using GD.SaveEditor.Presentation.Views;
using GD.SaveEditor.Services.Settings;
using GD.SaveEditor.Services.Theming;

namespace GD.SaveEditor;

/// <summary>Composition root: builds the DI container and creates the main window from it.</summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    /// <summary>Root service provider. Prefer constructor injection; use this only where Avalonia creates objects for you.</summary>
    public static IServiceProvider Services =>
        (Current as App)?._services ?? throw new InvalidOperationException("Services are not initialized yet.");

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
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

            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
            desktop.MainWindow = _services.GetRequiredService<MainWindow>();
            desktop.Exit += (_, _) => _services?.Dispose();

            // Auto-update runs in the background and reports through toasts:
            // download progress, then a countdown before the editor restarts into the new version.
            desktop.MainWindow.Opened += (_, _) => _ = RunAutoUpdateAsync(_services);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task RunAutoUpdateAsync(IServiceProvider services)
    {
        try
        {
            await services.GetRequiredService<IAutoUpdater>().RunOnStartupAsync();
        }
        catch (Exception ex)
        {
            // An update problem must never take the editor down.
            System.Diagnostics.Debug.WriteLine($"Auto-update failed: {ex}");
        }
    }
}
