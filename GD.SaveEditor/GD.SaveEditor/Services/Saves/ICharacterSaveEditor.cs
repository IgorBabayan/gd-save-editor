using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Services.Saves;

/// <summary>An item of the save file put on a bag: which item (by where it was read) and its new top-left cell.</summary>
/// <param name="FromBag">Bag the item was read from.</param>
/// <param name="FromIndex">Position of the item in that bag's list.</param>
public sealed record PlacedItem(int FromBag, int FromIndex, int X, int Y);

/// <summary>Changes made in the editor to one character; <c>null</c> values stay as they are in the file.</summary>
/// <param name="Money">New amount of iron bits.</param>
/// <param name="Bags">
/// New contents of every bag, in bag order: each item of the save file must be placed exactly once.
/// </param>
public sealed record CharacterChanges(uint? Money = null, IReadOnlyList<IReadOnlyList<PlacedItem>>? Bags = null)
{
    public bool IsEmpty => Money is null && Bags is null;
}

public interface ICharacterSaveEditor
{
    /// <summary>
    /// The contents of the character's <c>player.gdc</c> with <paramref name="changes"/> applied, encrypted like the
    /// game does; the file itself is not touched (see <see cref="ICharacterSaveWriter"/>).
    /// </summary>
    /// <exception cref="System.IO.InvalidDataException">
    /// The file has a part the editor does not know (<see cref="CharacterSave.RewriteError"/>), the changes do not fit
    /// it, or the result did not read back as expected.
    /// </exception>
    byte[] Apply(CharacterSave character, CharacterChanges changes);
}
