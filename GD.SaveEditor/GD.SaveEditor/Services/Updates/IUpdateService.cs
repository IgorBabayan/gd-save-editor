namespace GD.SaveEditor.Services.Updates;

/// <summary>Checks GitHub Releases for a newer version and installs it.</summary>
public interface IUpdateService
{
    /// <summary>Version of the running application (Major.Minor.Build).</summary>
    Version CurrentVersion { get; }

    /// <summary>
    /// True when the app runs from an installed copy (the Inno Setup uninstaller sits next to the exe).
    /// Updates are never applied to development builds or unpacked copies.
    /// </summary>
    bool IsInstalled { get; }

    /// <summary>Returns the latest release if it is newer than <see cref="CurrentVersion"/>, otherwise <c>null</c>.</summary>
    Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default);

    /// <summary>Downloads (and verifies) the installer, returning the local file path.</summary>
    Task<string> DownloadAsync(UpdateInfo update, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the installer silently. The installer closes this app, installs the update and starts the app again,
    /// so the caller must shut the application down right after this returns.
    /// </summary>
    void LaunchInstaller(string installerPath);

    /// <summary>Removes installers downloaded by previous updates. Never throws.</summary>
    void CleanupDownloads();
}
