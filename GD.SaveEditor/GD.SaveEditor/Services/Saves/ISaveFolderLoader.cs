using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Services.Saves;

public interface ISaveFolderLoader
{
    /// <summary>Reads every character of <paramref name="saveFolder"/> in the background; unreadable files end up in the errors.</summary>
    /// <exception cref="System.IO.IOException">The folder cannot be read.</exception>
    Task<SaveFolderContent> LoadAsync(string saveFolder, CancellationToken cancellationToken = default);

    /// <summary>Reads one character file again in the background.</summary>
    /// <exception cref="System.IO.InvalidDataException">The file is not a Grim Dawn character or is damaged.</exception>
    /// <exception cref="System.IO.IOException">The file cannot be read.</exception>
    Task<CharacterSave> LoadCharacterAsync(string filePath, CancellationToken cancellationToken = default);
}
