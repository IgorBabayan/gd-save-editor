using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Services.Saves;

/// <summary>Changes made in the editor to one character; <c>null</c> values stay as they are in the file.</summary>
/// <param name="Money">New amount of iron bits.</param>
public sealed record CharacterChanges(uint? Money = null)
{
    public bool IsEmpty => Money is null;
}

public interface ICharacterSaveEditor
{
    /// <summary>
    /// The contents of the character's <c>player.gdc</c> with <paramref name="changes"/> applied, encrypted like the
    /// game does; the file itself is not touched (see <see cref="ICharacterSaveWriter"/>).
    /// </summary>
    /// <exception cref="System.IO.InvalidDataException">
    /// The file has a part the editor does not know (<see cref="CharacterSave.RewriteError"/>), or the result did not
    /// read back as expected.
    /// </exception>
    byte[] Apply(CharacterSave character, CharacterChanges changes);
}
