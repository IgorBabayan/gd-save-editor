using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using GD.SaveEditor.Presentation.Services.Toasts;
using GD.SaveEditor.Services.Notifications;
using GD.SaveEditor.Services.Settings;
using GD.SaveEditor.Services.Updates;

namespace GD.SaveEditor.Presentation.Services;

public sealed partial class AutoUpdater(
    ISettingsService settingsService,
    IUpdateService updateService,
    IToastService toastService,
    INotificationService notificationService) : ObservableObject, IAutoUpdater
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(15);

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private UpdateInfo? _availableUpdate;

    public bool CanInstall => updateService.IsInstalled;

    public async Task RunOnStartupAsync()
    {
        if (!settingsService.Current.AutoUpdate || !updateService.IsInstalled || IsBusy)
            return;

        UpdateInfo? update;
        IsBusy = true;
        try
        {
            update = await CheckCoreAsync();
        }
        catch (Exception ex) when (IsCheckError(ex))
        {
            // Offline, GitHub unavailable or rate-limited: silently keep the current version.
            Debug.WriteLine($"Update check failed: {ex.Message}");
            return;
        }
        finally
        {
            IsBusy = false;
        }

        if (update is null)
        {
            // Up to date: remove installers left over from the previous update.
            updateService.CleanupDownloads();
            return;
        }

        await DownloadAndInstallAsync();
    }

    public async Task<bool> CheckForUpdatesAsync()
    {
        if (IsBusy)
            return AvailableUpdate is not null;

        IsBusy = true;
        try
        {
            var update = await CheckCoreAsync();

            if (update is null)
                toastService.Show("You are up to date", NotificationSeverity.Success);
            else
                toastService.Show($"A new version {update.Version.ToString(3)} is available");

            return update is not null;
        }
        catch (Exception ex) when (IsCheckError(ex))
        {
            toastService.Show("Could not check for updates", NotificationSeverity.Error);
            notificationService.Notify("Update check failed", Describe(ex), NotificationSeverity.Error);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DownloadAndInstallAsync(int? restartDelaySeconds = null)
    {
        var update = AvailableUpdate;
        if (update is null || IsBusy || !updateService.IsInstalled)
            return;

        IsBusy = true;
        using var cancellation = new CancellationTokenSource();
        var version = update.Version.ToString(3);

        var toast = toastService.ShowPersistent("A new version is downloading");
        toast.Details = $"Version {version}";
        toast.Progress = 0;
        toast.SetAction("Cancel", cancellation.Cancel);

        try
        {
            // Progress<T> is created on the UI thread, so reports arrive on the UI thread.
            var progress = new Progress<DownloadProgress>(p =>
            {
                toast.Progress = p.Fraction ?? 0;
                toast.Details = p.TotalBytes > 0
                    ? $"Version {version} · {ToMegabytes(p.BytesReceived):0.0} of {ToMegabytes(p.TotalBytes):0.0} MB"
                    : $"Version {version} · {ToMegabytes(p.BytesReceived):0.0} MB";
            });

            var installerPath = await updateService.DownloadAsync(update, progress, cancellation.Token);
            toastService.Dismiss(toast);

            if (!await CountdownAsync(restartDelaySeconds ?? settingsService.Current.RestartDelaySeconds))
                return;

            updateService.LaunchInstaller(installerPath);

            // Exit now so the installer can replace the files; it starts the new version afterwards.
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            toastService.Show("Update cancelled");
        }
        catch (Exception ex) when (IsInstallError(ex))
        {
            // Network problem, bad download or the UAC prompt was declined: keep running the current version.
            Debug.WriteLine($"Update failed: {ex}");
            toastService.Show("Update failed", NotificationSeverity.Error);
            notificationService.Notify("Update failed", Describe(ex), NotificationSeverity.Error);
        }
        finally
        {
            toastService.Dismiss(toast);
            IsBusy = false;
        }
    }

    /// <summary>Shows "Application will restart in N seconds" and ticks it down.</summary>
    /// <returns><c>false</c> if the user cancelled the restart.</returns>
    private async Task<bool> CountdownAsync(int restartDelaySeconds)
    {
        var seconds = Math.Clamp(restartDelaySeconds, 0, AppSettings.MaxRestartDelaySeconds);
        if (seconds == 0)
            return true;

        using var cancellation = new CancellationTokenSource();
        var toast = toastService.ShowPersistent(RestartMessage(seconds));
        toast.Details = "The new version starts automatically after installation.";
        toast.SetAction("Cancel", cancellation.Cancel);

        try
        {
            for (var remaining = seconds; remaining > 0; remaining--)
            {
                toast.Message = RestartMessage(remaining);
                toast.Progress = (double)remaining / seconds;
                await Task.Delay(TimeSpan.FromSeconds(1), cancellation.Token);
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            toastService.Show("Restart cancelled. Use \"Download and update\" in Settings to install later.");
            return false;
        }
        finally
        {
            toastService.Dismiss(toast);
        }
    }

    private async Task<UpdateInfo?> CheckCoreAsync()
    {
        using var timeout = new CancellationTokenSource(CheckTimeout);
        var update = await updateService.CheckForUpdateAsync(timeout.Token);
        AvailableUpdate = update;
        return update;
    }

    private static string RestartMessage(int seconds) =>
        seconds == 1
            ? "Application will restart in 1 second"
            : $"Application will restart in {seconds} seconds";

    private static bool IsCheckError(Exception ex) =>
        ex is HttpRequestException or OperationCanceledException or JsonException or IOException;

    private static bool IsInstallError(Exception ex) =>
        ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException
            or Win32Exception;

    private static string Describe(Exception ex) =>
        ex is OperationCanceledException ? "The request timed out." : ex.Message;

    private static double ToMegabytes(long bytes) => bytes / 1024d / 1024d;
}
