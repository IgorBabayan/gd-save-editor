using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Services.Saves;

/// <summary>A saved character, read back from the written file, and where the previous file was kept.</summary>
public sealed record CharacterSaveResult(CharacterSave Character, string BackupPath);

/// <summary>Thrown when the character file changed on disk after it was read (e.g. the game saved it).</summary>
public sealed class SaveFileChangedException(string message) : System.IO.IOException(message);

public interface ICharacterSaveWriter
{
    /// <summary>
    /// Writes <paramref name="data"/> into the character's <c>player.gdc</c>, keeping the previous file as a
    /// timestamped backup next to it, then reads the written file back.
    /// </summary>
    /// <exception cref="SaveFileChangedException">The file changed since it was read.</exception>
    Task<CharacterSaveResult> SaveAsync(CharacterSave character, byte[] data, CancellationToken cancellationToken = default);
}
