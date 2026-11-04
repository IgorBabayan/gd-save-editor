using System.ComponentModel;
using GD.SaveEditor.Services.Updates;

namespace GD.SaveEditor.Presentation.Services;

/// <summary>
/// Checks for, downloads and installs updates in the background, reporting progress with toasts.
/// Must be used from the UI thread.
/// </summary>
public interface IAutoUpdater : INotifyPropertyChanged
{
    /// <summary>A check or a download/install is running.</summary>
    bool IsBusy { get; }

    /// <summary>Newer release found by the last check, or <c>null</c>.</summary>
    UpdateInfo? AvailableUpdate { get; }

    /// <summary>Updates can only be installed over a copy that was installed with the installer.</summary>
    bool CanInstall { get; }

    /// <summary>
    /// Runs once after the main window is shown: if auto-update is enabled and a newer release exists,
    /// downloads it, counts down and restarts into the new version. Never throws.
    /// </summary>
    Task RunOnStartupAsync();

    /// <summary>Checks GitHub and shows the result as a toast.</summary>
    /// <returns><c>true</c> if a newer version is available.</returns>
    Task<bool> CheckForUpdatesAsync();

    /// <summary>
    /// Downloads <see cref="AvailableUpdate"/>, counts down and restarts into the new version.
    /// Problems are reported with toasts; never throws.
    /// </summary>
    /// <param name="restartDelaySeconds">
    /// Countdown length; <c>null</c> uses the saved <c>RestartDelaySeconds</c> setting.
    /// </param>
    Task DownloadAndInstallAsync(int? restartDelaySeconds = null);
}
