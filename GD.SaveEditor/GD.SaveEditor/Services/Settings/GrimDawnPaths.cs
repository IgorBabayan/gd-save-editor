using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace GD.SaveEditor.Services.Settings;

public static partial class GrimDawnPaths
{
    /// <summary>Steam app id of Grim Dawn.</summary>
    private const string SteamAppId = "219990";

    /// <summary>GOG product id of Grim Dawn.</summary>
    private const string GogProductId = "1449651388";

    /// <summary>
    /// Default location of the character saves: <c>Documents\My Games\Grim Dawn\save\main</c>.
    /// The Documents folder follows OneDrive redirection (e.g. <c>C:\Users\omega\OneDrive\Documents</c>).
    /// </summary>
    public static string DefaultSaveFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "My Games", "Grim Dawn", "save", "main");

    /// <summary>
    /// The Grim Dawn installation folder found on this computer (Steam libraries, then GOG), or <c>null</c>.
    /// A folder counts only when it holds <c>database\database.arz</c>.
    /// </summary>
    public static string? FindGameFolder() =>
        GetCandidateGameFolders().FirstOrDefault(IsGameFolder);

    /// <summary>Whether <paramref name="folder"/> looks like a Grim Dawn installation.</summary>
    public static bool IsGameFolder(string? folder) =>
        !string.IsNullOrWhiteSpace(folder)
        && File.Exists(Path.Combine(folder.Trim(), "database", "database.arz"));

    private static IEnumerable<string> GetCandidateGameFolders()
    {
        if (ReadRegistry(RegistryHive.LocalMachine, $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App {SteamAppId}", "InstallLocation") is { } steamUninstall)
            yield return steamUninstall;

        var steamPath = ReadRegistry(RegistryHive.CurrentUser, @"SOFTWARE\Valve\Steam", "SteamPath");
        if (!string.IsNullOrWhiteSpace(steamPath))
        {
            foreach (var library in GetSteamLibraries(steamPath))
                yield return Path.Combine(library, "steamapps", "common", "Grim Dawn");
        }

        if (ReadRegistry(RegistryHive.LocalMachine, $@"SOFTWARE\GOG.com\Games\{GogProductId}", "path") is { } gog)
            yield return gog;

        yield return @"C:\Program Files (x86)\Steam\steamapps\common\Grim Dawn";
        yield return @"C:\Program Files\Steam\steamapps\common\Grim Dawn";
        yield return @"C:\GOG Games\Grim Dawn";
    }

    /// <summary>The Steam folder itself plus every <c>"path"</c> of <c>steamapps\libraryfolders.vdf</c>.</summary>
    private static IEnumerable<string> GetSteamLibraries(string steamPath)
    {
        var libraries = new List<string> { steamPath.Replace('/', Path.DirectorySeparatorChar) };
        try
        {
            var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (Match match in VdfPathRegex().Matches(File.ReadAllText(vdf)))
                    libraries.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only the default library is searched then.
        }

        return libraries.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Reads a string from the 64-bit view, then from the 32-bit view of the registry.</summary>
    private static string? ReadRegistry(RegistryHive hive, string keyPath, string valueName)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var key = root.OpenSubKey(keyPath);
                if (key?.GetValue(valueName) is string value && !string.IsNullOrWhiteSpace(value))
                    return value;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // Try the other view.
            }
        }

        return null;
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex VdfPathRegex();
}
