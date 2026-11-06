using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using GD.SaveEditor.Services.GameData.Archives;
using GD.SaveEditor.Services.Settings;

namespace GD.SaveEditor.Services.GameData;

/// <summary>Where a game file was taken from.</summary>
public enum GameFileOrigin
{
    /// <summary>Downloaded from GrimTools in this run.</summary>
    Downloaded,

    /// <summary>Read from the Grim Dawn installation folder.</summary>
    GameFolder,

    /// <summary>Downloaded in an earlier run (neither GrimTools nor the game folder had it now).</summary>
    EarlierDownload
}

/// <summary>A game file ready to be read, with where it came from.</summary>
/// <param name="FailureReason">Why it could not be downloaded, when it was taken from elsewhere.</param>
public sealed record ProvidedGameFile(GameFile File, string LocalPath, GameFileOrigin Origin, string? FailureReason);

/// <summary>Gets the game files the database is built from.</summary>
public interface IGameFileProvider
{
    /// <summary>Base address the files are downloaded from: <c>https://www.grimtools.com/db/</c>.</summary>
    Uri DownloadBaseAddress { get; }

    /// <summary>
    /// Downloads <paramref name="file"/> from GrimTools. When that fails (or the response is not a game file), it is
    /// read from the Grim Dawn game folder, and as a last resort from an earlier download.
    /// </summary>
    /// <param name="progress">Download progress (0..1) and a short detail text.</param>
    /// <returns>The file, or <c>null</c> with <paramref name="failureReason"/> set when it is not available anywhere.</returns>
    Task<(ProvidedGameFile? File, string? FailureReason)> GetAsync(
        GameFile file,
        IProgress<(double Fraction, string Detail)>? progress,
        CancellationToken cancellationToken);
}

public sealed class GameFileProvider(ISettingsService settingsService) : IGameFileProvider
{
    private static readonly HttpClient Http = CreateHttpClient();

    public Uri DownloadBaseAddress { get; } = new("https://www.grimtools.com/db/");

    public async Task<(ProvidedGameFile? File, string? FailureReason)> GetAsync(
        GameFile file,
        IProgress<(double Fraction, string Detail)>? progress,
        CancellationToken cancellationToken)
    {
        var cachePath = Path.Combine(GameDataPaths.GameFilesDirectory, ToLocalPath(file.RelativePath));

        // 1. GrimTools (not for the files only read from the installation)
        var downloadFailure = file.GameFolderOnly
            ? $"{file.FileName} is only read from the game folder"
            : await TryDownloadAsync(file, cachePath, progress, cancellationToken);
        if (downloadFailure is null)
            return (new ProvidedGameFile(file, cachePath, GameFileOrigin.Downloaded, null), null);

        // 2. Grim Dawn installation folder
        var gameFolder = settingsService.Current.GameFolderPath is { } configured && GrimDawnPaths.IsGameFolder(configured)
            ? configured.Trim()
            : GrimDawnPaths.FindGameFolder();
        if (gameFolder is not null)
        {
            var installed = Path.Combine(gameFolder, ToLocalPath(file.RelativePath));
            if (File.Exists(installed) && IsValid(file, installed))
            {
                progress?.Report((1, $"Using {installed}"));
                return (new ProvidedGameFile(file, installed, GameFileOrigin.GameFolder, downloadFailure), null);
            }
        }

        // 3. An earlier download
        if (!file.GameFolderOnly && File.Exists(cachePath) && IsValid(file, cachePath))
        {
            progress?.Report((1, $"Using the earlier download {cachePath}"));
            return (new ProvidedGameFile(file, cachePath, GameFileOrigin.EarlierDownload, downloadFailure), null);
        }

        var notInstalled = gameFolder is null
            ? "no Grim Dawn installation was found (set it in Settings)"
            : $"it is not in the game folder {gameFolder}";
        return (null, $"{downloadFailure}; {notInstalled}");
    }

    /// <summary>Downloads into <paramref name="targetPath"/>; returns why it failed, or <c>null</c> on success.</summary>
    private async Task<string?> TryDownloadAsync(
        GameFile file,
        string targetPath,
        IProgress<(double Fraction, string Detail)>? progress,
        CancellationToken cancellationToken)
    {
        var url = new Uri(DownloadBaseAddress, file.RelativePath);
        var tempPath = targetPath + ".download";
        progress?.Report((0, $"Downloading {url}"));

        try
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return $"GrimTools answered {(int)response.StatusCode} {response.ReasonPhrase} for {url}";

            // An unknown path of the site returns its web page with 200 OK: that is not the file.
            if (response.Content.Headers.ContentType?.MediaType is { } mediaType
                && mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
                return $"GrimTools returned a web page instead of {file.FileName}";

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            var total = response.Content.Headers.ContentLength;

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long received = 0;
                var lastReport = Environment.TickCount64;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;

                    if (Environment.TickCount64 - lastReport >= 100)
                    {
                        lastReport = Environment.TickCount64;
                        progress?.Report((total is > 0 ? (double)received / total.Value : 0, $"{FormatSize(received)}{(total is > 0 ? $" of {FormatSize(total.Value)}" : string.Empty)}"));
                    }
                }
            }

            if (!IsValid(file, tempPath))
            {
                File.Delete(tempPath);
                return $"GrimTools did not return a valid {file.FileName}";
            }

            File.Move(tempPath, targetPath, overwrite: true);
            progress?.Report((1, $"Downloaded {FormatSize(new FileInfo(targetPath).Length)}"));
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException
                                       || ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            TryDelete(tempPath);
            return $"{file.FileName} could not be downloaded from GrimTools ({ex.Message})";
        }
        catch (OperationCanceledException)
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static bool IsValid(GameFile file, string path) =>
        file.Kind == GameFileKind.Database ? ArzReader.LooksValid(path) : ArcReader.LooksValid(path);

    private static string ToLocalPath(string relativePath) => relativePath.Replace('/', Path.DirectorySeparatorChar);

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / 1024d / 1024d:0.0} MB",
        >= 1024 => $"{bytes / 1024d:0} KB",
        _ => $"{bytes} B"
    };

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover partial download is replaced by the next one.
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(20)
        })
        {
            // Large files: the request may run long; cancellation stops it earlier.
            Timeout = TimeSpan.FromMinutes(30)
        };

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GD.SaveEditor", version));
        return client;
    }
}
