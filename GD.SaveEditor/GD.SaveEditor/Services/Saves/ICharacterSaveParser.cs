using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Services.Saves;

public interface ICharacterSaveParser
{
    /// <summary>Reads the character in <paramref name="filePath"/> (<c>player.gdc</c>).</summary>
    /// <exception cref="System.IO.InvalidDataException">The file is not a Grim Dawn character or is damaged.</exception>
    /// <exception cref="System.IO.IOException">The file cannot be read.</exception>
    CharacterSave Parse(string filePath);
}
