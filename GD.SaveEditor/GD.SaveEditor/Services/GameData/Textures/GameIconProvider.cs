using System.Collections.Concurrent;
using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using GD.SaveEditor.Services.GameData.Archives;
using GD.SaveEditor.Services.Settings;

namespace GD.SaveEditor.Services.GameData.Textures;

/// <summary>Item icons read from the game's resource archives (<c>Items.arc</c> of the game and its expansions).</summary>
public interface IGameIconProvider
{
    /// <summary>
    /// The texture <paramref name="resourcePath"/> (e.g. <c>items/gearhead/bitmaps/a01_head001.tex</c>) as an immutable
    /// bitmap, or <c>null</c> when no archive has it or it cannot be decoded. Can be called from any thread; different
    /// icons are decoded in parallel, the same icon only once.
    /// </summary>
    Bitmap? GetIcon(string? resourcePath);

    /// <summary>Closes the archives and forgets the icons, e.g. after the game files were downloaded again.</summary>
    void ClearCache();
}

public sealed class GameIconProvider(ISettingsService settingsService) : IGameIconProvider, IDisposable
{
    /// <summary>Folders inside a game folder that have a <c>resources</c> folder; expansions first, they override.</summary>
    private static readonly string[] ContentFolders = ["gdx3", "gdx2", "gdx1", ""];

    /// <summary>
    /// Decoded icons. A <see cref="Lazy{T}"/> per path: the decoding (the slow part) runs outside any lock, so icons
    /// requested by several worker threads at once (a long item list) are decoded side by side, each one once.
    /// </summary>
    private readonly ConcurrentDictionary<string, Lazy<Bitmap?>> _icons = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Opened archives; guarded by <see cref="_lock"/> (opening reads the whole file table).</summary>
    private readonly Dictionary<string, List<OpenArchive>> _archives = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    public Bitmap? GetIcon(string? resourcePath)
    {
        if (string.IsNullOrWhiteSpace(resourcePath))
            return null;

        var path = Normalize(resourcePath);
        return _icons.GetOrAdd(path, static (key, self) => new Lazy<Bitmap?>(() => self.Load(key)), this).Value;
    }

    public void ClearCache()
    {
        lock (_lock)
        {
            _icons.Clear();
            foreach (var archive in _archives.Values.SelectMany(a => a))
                archive.Reader.Dispose();
            _archives.Clear();
        }
    }

    public void Dispose() => ClearCache();

    private Bitmap? Load(string path)
    {
        // "items/gearhead/bitmaps/x.tex": the first folder names the archive (Items.arc), the rest is the file in it.
        var separator = path.IndexOf('/');
        if (separator <= 0)
            return null;

        var archiveName = path[..separator] + ".arc";
        var innerPath = path[(separator + 1)..];

        List<OpenArchive> archives;
        lock (_lock)
            archives = GetArchives(archiveName);

        foreach (var archive in archives)
        {
            if (!archive.Entries.TryGetValue(innerPath, out var entry) && !archive.Entries.TryGetValue(path, out entry))
                continue;

            try
            {
                var image = TexDecoder.Decode(archive.Reader.ReadFile(entry));
                return CreateBitmap(image);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or ObjectDisposedException)
            {
                // A texture this decoder cannot read: try another archive, else no icon.
            }
        }

        return null;
    }

    /// <summary>Copies the decoded BGRA pixels into an immutable bitmap (straight alpha, as the textures store it).</summary>
    private static unsafe Bitmap CreateBitmap(DecodedImage image)
    {
        fixed (byte* pixels = image.Bgra)
        {
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Unpremul, (nint)pixels,
                new PixelSize(image.Width, image.Height), new Vector(96, 96), image.Width * 4);
        }
    }

    /// <summary>Every copy of <paramref name="archiveName"/> found (game folder and downloaded game files), opened once.</summary>
    private List<OpenArchive> GetArchives(string archiveName)
    {
        if (_archives.TryGetValue(archiveName, out var opened))
            return opened;

        opened = [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in GetRoots())
        foreach (var folder in ContentFolders)
        {
            var file = Path.Combine(root, folder, "resources", archiveName);
            if (!File.Exists(file) || !seen.Add(Path.GetFullPath(file)))
                continue;

            try
            {
                var reader = ArcReader.Open(file);
                var entries = new Dictionary<string, ArcEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in reader.Entries)
                    entries.TryAdd(Normalize(entry.Name), entry);
                opened.Add(new OpenArchive(reader, entries));
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                // Damaged or locked archive: icons come from the other copies, if any.
            }
        }

        _archives[archiveName] = opened;
        return opened;
    }

    /// <summary>The configured (or found) Grim Dawn folder, then the game files downloaded by the database generator.</summary>
    private IEnumerable<string> GetRoots()
    {
        var configured = settingsService.Current.GameFolderPath;
        var gameFolder = GrimDawnPaths.IsGameFolder(configured) ? configured!.Trim() : GrimDawnPaths.FindGameFolder();
        if (gameFolder is not null)
            yield return gameFolder;

        yield return GameDataPaths.GameFilesDirectory;
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');

    private sealed record OpenArchive(ArcReader Reader, Dictionary<string, ArcEntry> Entries);
}
