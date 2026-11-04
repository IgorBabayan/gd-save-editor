using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace GD.SaveEditor.Services.Updates;

/// <summary>
/// Uses the latest published GitHub Release of <c>IgorBabayan/gd-save-editor</c>
/// (created by <c>.github/workflows/release-installer.yml</c>) as the update source.
/// </summary>
public sealed class GitHubUpdateService : IUpdateService, IDisposable
{
    private const string Owner = "IgorBabayan";
    private const string Repository = "gd-save-editor";
    private const string InstallerPrefix = "GD.SaveEditor-Setup";

    /// <summary>Inno Setup always names the uninstaller of the first install like this.</summary>
    private const string UninstallerFileName = "unins000.exe";

    private static readonly Uri LatestReleaseUri =
        new($"https://api.github.com/repos/{Owner}/{Repository}/releases/latest");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private static readonly string DownloadFolder =
        Path.Combine(Path.GetTempPath(), "GD.SaveEditor", "Updates");

    private readonly HttpClient _http;

    public GitHubUpdateService()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
        CurrentVersion = Normalize(version);
        IsInstalled = File.Exists(Path.Combine(AppContext.BaseDirectory, UninstallerFileName));

        // Timeouts are controlled per call with cancellation tokens (downloads may take a while).
        _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        // GitHub rejects API requests without a User-Agent.
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GD.SaveEditor", CurrentVersion.ToString()));
    }

    public Version CurrentVersion { get; }

    public bool IsInstalled { get; }

    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // No published release yet.
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        await using var json = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var release = await JsonSerializer.DeserializeAsync<ReleaseDto>(json, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);

        if (release is null || release.Draft || release.Prerelease || !TryParseVersion(release.TagName, out var latest))
            return null;

        if (latest <= CurrentVersion)
            return null;

        var asset = SelectInstaller(release.Assets);
        if (asset is null || !Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var downloadUrl))
            return null;

        Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out var pageUrl);

        return new UpdateInfo(
            latest,
            Path.GetFileName(asset.Name!),
            downloadUrl,
            asset.Size,
            ParseSha256(asset.Digest),
            pageUrl);
    }

    public async Task<string> DownloadAsync(
        UpdateInfo update,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        Directory.CreateDirectory(DownloadFolder);
        var targetPath = Path.Combine(DownloadFolder, update.InstallerName);
        var partialPath = targetPath + ".part";

        try
        {
            using (var response = await _http
                       .GetAsync(update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                       .ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                var total = response.Content.Headers.ContentLength ?? update.InstallerSize;
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var target = new FileStream(
                    partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

                var buffer = new byte[81920];
                long received = 0;
                int read;
                progress?.Report(new DownloadProgress(0, total));

                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    received += read;
                    progress?.Report(new DownloadProgress(received, total));
                }
            }

            await VerifyAsync(partialPath, update, cancellationToken).ConfigureAwait(false);

            File.Move(partialPath, targetPath, overwrite: true);
            return targetPath;
        }
        catch
        {
            TryDelete(partialPath);
            throw;
        }
    }

    public void LaunchInstaller(string installerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);

        var logPath = Path.Combine(DownloadFolder, "install.log");

        // /VERYSILENT              no wizard and no progress window
        // /CLOSEAPPLICATIONS       close this app if it is still running when files are replaced
        // /NORESTARTAPPLICATIONS   do not let Restart Manager start it again...
        // /RELAUNCH=1              ...because the installer script starts it itself after installation
        Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /NORESTARTAPPLICATIONS /RELAUNCH=1 /LOG=\"{logPath}\"",
            // Shell execute lets Windows show the UAC prompt if the app was installed for all users.
            UseShellExecute = true,
            WorkingDirectory = DownloadFolder
        });
    }

    public void CleanupDownloads()
    {
        try
        {
            if (Directory.Exists(DownloadFolder))
                Directory.Delete(DownloadFolder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file may still be locked by a finishing installer; it will be removed next time.
            Debug.WriteLine($"Update cleanup failed: {ex.Message}");
        }
    }

    public void Dispose() => _http.Dispose();

    private static async Task VerifyAsync(string path, UpdateInfo update, CancellationToken cancellationToken)
    {
        var length = new FileInfo(path).Length;
        if (update.InstallerSize > 0 && length != update.InstallerSize)
            throw new InvalidDataException(
                $"Downloaded installer has {length} bytes, expected {update.InstallerSize}.");

        if (update.InstallerSha256 is null)
            return;

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));

        if (!string.Equals(hash, update.InstallerSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Downloaded installer failed the SHA-256 check.");
    }

    private static AssetDto? SelectInstaller(IReadOnlyList<AssetDto>? assets)
    {
        if (assets is null)
            return null;

        var installers = assets
            .Where(a => a.Name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        return installers.FirstOrDefault(a => a.Name!.StartsWith(InstallerPrefix, StringComparison.OrdinalIgnoreCase))
               ?? (installers.Count == 1 ? installers[0] : null);
    }

    private static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag))
            return false;

        var text = tag.Trim().TrimStart('v', 'V');
        // Ignore any suffix such as "-beta" or "+build".
        var end = text.IndexOfAny(['-', '+', ' ']);
        if (end >= 0)
            text = text[..end];

        if (!Version.TryParse(text, out var parsed))
            return false;

        version = Normalize(parsed);
        return true;
    }

    /// <summary>Compares only Major.Minor.Build so 0.1.12 and 0.1.12.0 are equal.</summary>
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    /// <summary>GitHub publishes asset digests as <c>sha256:&lt;hex&gt;</c>.</summary>
    private static string? ParseSha256(string? digest)
    {
        const string prefix = "sha256:";
        return digest is not null && digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? digest[prefix.Length..]
            : null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Could not delete {path}: {ex.Message}");
        }
    }

    // ===== GitHub API payload (only the fields we use) =====

    private sealed record ReleaseDto(
        string? TagName,
        string? HtmlUrl,
        bool Draft,
        bool Prerelease,
        IReadOnlyList<AssetDto>? Assets);

    private sealed record AssetDto(
        string? Name,
        string? BrowserDownloadUrl,
        long Size,
        string? Digest);
}
