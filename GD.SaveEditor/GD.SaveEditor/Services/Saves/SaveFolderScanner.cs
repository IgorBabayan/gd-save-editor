using System.IO;

namespace GD.SaveEditor.Services.Saves;

public sealed class SaveFolderScanner : ISaveFolderScanner
{
    public const string CharacterFileName = "player.gdc";

    public IReadOnlyList<string> FindCharacterFiles(string saveFolder)
    {
        if (string.IsNullOrWhiteSpace(saveFolder) || !Directory.Exists(saveFolder))
            return [];

        // Every character lives in its own "_Name" folder; folders without player.gdc (e.g. "user") are skipped.
        return Directory.EnumerateDirectories(saveFolder)
            .Select(folder => Path.Combine(folder, CharacterFileName))
            .Where(File.Exists)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
