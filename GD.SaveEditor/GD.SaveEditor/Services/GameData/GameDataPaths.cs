using System.IO;

namespace GD.SaveEditor.Services.GameData;

/// <summary>
/// Game data kept in the editor's config folder <c>%APPDATA%\GD.SaveEditor</c> (the same folder as <c>settings.json</c>).
/// </summary>
public static class GameDataPaths
{
    public static string ConfigDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GD.SaveEditor");

    /// <summary>SQLite database with the game records (items, ...), texts and the resource index.</summary>
    public static string DatabaseFile { get; } = Path.Combine(ConfigDirectory, "database.db");

    /// <summary>Downloaded game files, by their path in the game folder (e.g. <c>GameFiles\database\database.arz</c>).</summary>
    public static string GameFilesDirectory { get; } = Path.Combine(ConfigDirectory, "GameFiles");

    /// <summary>One <c>.sql</c> file per migration applied to <see cref="DatabaseFile"/>.</summary>
    public static string MigrationsDirectory { get; } = Path.Combine(ConfigDirectory, "Migrations");
}
