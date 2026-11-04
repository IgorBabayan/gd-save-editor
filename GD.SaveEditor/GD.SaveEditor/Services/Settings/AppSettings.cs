using GD.SaveEditor.Services.Theming;

namespace GD.SaveEditor.Services.Settings;

/// <summary>User preferences persisted between application runs.</summary>
public sealed record AppSettings
{
    public AppTheme Theme { get; init; } = AppTheme.DefaultNight;

    /// <summary>Folder with Grim Dawn character saves (one <c>_Name\player.gdc</c> folder per character).</summary>
    public string? SaveFolderPath { get; init; }

    /// <summary>
    /// Grim Dawn installation folder. The database generator copies the game's .arz / .arc files from here
    /// when they cannot be downloaded.
    /// </summary>
    public string? GameFolderPath { get; init; }
}
