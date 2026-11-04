namespace GD.SaveEditor.Services.Updates;

/// <summary>A newer release found on GitHub, together with its installer asset.</summary>
/// <param name="Version">Release version parsed from the tag (e.g. <c>v0.1.12</c> → 0.1.12).</param>
/// <param name="InstallerName">File name of the installer asset.</param>
/// <param name="InstallerUrl">Direct download URL of the installer asset.</param>
/// <param name="InstallerSize">Size of the installer in bytes, or 0 if unknown.</param>
/// <param name="InstallerSha256">Expected SHA-256 (lower-case hex) published by GitHub, if available.</param>
/// <param name="ReleasePageUrl">Web page of the release.</param>
public sealed record UpdateInfo(
    Version Version,
    string InstallerName,
    Uri InstallerUrl,
    long InstallerSize,
    string? InstallerSha256,
    Uri? ReleasePageUrl);

/// <summary>Download progress report.</summary>
/// <param name="BytesReceived">Bytes downloaded so far.</param>
/// <param name="TotalBytes">Total size in bytes, or 0 if unknown.</param>
public readonly record struct DownloadProgress(long BytesReceived, long TotalBytes)
{
    /// <summary>Completed fraction in the range 0..1, or <c>null</c> if the total size is unknown.</summary>
    public double? Fraction => TotalBytes > 0 ? Math.Clamp((double)BytesReceived / TotalBytes, 0, 1) : null;
}
