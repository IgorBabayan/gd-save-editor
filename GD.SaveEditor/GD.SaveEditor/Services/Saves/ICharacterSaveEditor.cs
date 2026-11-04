using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Services.Saves;

/// <summary>
/// An item put on a bag at its new top-left cell: an item of the save file (by where it was read), possibly changed in
/// the item builder, or a new one.
/// </summary>
/// <param name="FromBag">Bag the item was read from; <see cref="New"/> for an item made in the editor.</param>
/// <param name="FromIndex">Position of the item in that bag's list; <see cref="New"/> for an item made in the editor.</param>
/// <param name="Item">
/// The item to write. <c>null</c> keeps the item of the save file exactly as it was read (only its cell changes); a
/// value writes this item instead: a changed item of the save file, or a new one.
/// </param>
public sealed record PlacedItem(int FromBag, int FromIndex, int X, int Y, SaveItem? Item = null)
{
    /// <summary><see cref="FromBag"/> and <see cref="FromIndex"/> of an item made in the editor.</summary>
    public const int New = -1;

    /// <summary>The item was made in the editor: it is not in the save file yet.</summary>
    public bool IsNew => FromBag == New;

    /// <summary>A new item at a cell.</summary>
    public static PlacedItem Create(SaveItem item, int x, int y) => new(New, New, x, y, item);
}

/// <summary>Changes made in the editor to one character; <c>null</c> values stay as they are in the file.</summary>
/// <param name="Money">New amount of iron bits.</param>
/// <param name="Bags">
/// New contents of every bag, in bag order. Each item of the save file is placed at most once; one that is not placed
/// is removed from the character. New items (<see cref="PlacedItem.IsNew"/>) are added.
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
