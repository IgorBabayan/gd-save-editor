namespace GD.SaveEditor.Services.Saves.Models;

/// <summary>A character file that could not be read, and why.</summary>
public sealed record SaveFileError(string FilePath, string Message);

/// <summary>Everything read from the save folder.</summary>
public sealed record SaveFolderContent(
    IReadOnlyList<CharacterSave> Characters,
    IReadOnlyList<SaveFileError> Errors);
