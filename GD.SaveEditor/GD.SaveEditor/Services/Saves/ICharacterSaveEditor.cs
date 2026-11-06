using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Services.Saves;

/// <summary>
/// An item put on a bag (or a stash tab) at its new top-left cell: an item of the save file (by where it was read),
/// possibly changed in the item builder, or a new one.
/// </summary>
/// <param name="FromBag">Bag (or stash tab) the item was read from; <see cref="New"/> for an item made in the editor.</param>
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

/// <summary>An equipment slot that gets another item, or is emptied.</summary>
/// <param name="WeaponSet">1 or 2 for the weapon slots, 0 for armor and jewelry.</param>
/// <param name="Item">The item to put in the slot; <c>null</c> empties it.</param>
public sealed record EquipmentChange(EquipmentSlot Slot, int WeaponSet, SaveItem? Item);

/// <summary>A new reputation for the faction at <paramref name="Index"/> of the faction list (see <see cref="CharacterFactions"/>).</summary>
public sealed record FactionChange(int Index, float Value);

/// <summary>Changes made in the editor to one character; <c>null</c> values stay as they are in the file.</summary>
/// <param name="Money">New amount of iron bits.</param>
/// <param name="Bags">
/// New contents of every bag, in bag order. Each item of the save file is placed at most once; one that is not placed
/// is removed from the character. New items (<see cref="PlacedItem.IsNew"/>) are added.
/// </param>
/// <param name="Equipment">Equipment slots that get another item or are emptied; the other slots stay as they are.</param>
/// <param name="Profile">Name, sex, unlocked difficulty, Crucible tributes, unspent points and attributes.</param>
/// <param name="Waypoints">
/// The unlocked waypoints of Normal, Elite and Ultimate as they should be stored: they replace the three lists of the
/// file (see <see cref="CharacterWaypoints"/>).
/// </param>
/// <param name="Factions">
/// New reputation values; the other factions stay as they are. A changed faction is also marked as modified and
/// unlocked, so the game shows it in its faction window.
/// </param>
/// <param name="Stash">
/// New contents of every tab of the personal stash the character bought, in tab order, like <paramref name="Bags"/>.
/// </param>
public sealed record CharacterChanges(
    uint? Money = null,
    IReadOnlyList<IReadOnlyList<PlacedItem>>? Bags = null,
    IReadOnlyList<EquipmentChange>? Equipment = null,
    ProfileChanges? Profile = null,
    CharacterWaypoints? Waypoints = null,
    IReadOnlyList<FactionChange>? Factions = null,
    IReadOnlyList<IReadOnlyList<PlacedItem>>? Stash = null)
{
    public bool IsEmpty =>
        Money is null && Bags is null && Equipment is not { Count: > 0 } && Profile is null or { IsEmpty: true }
        && Waypoints is null && Factions is not { Count: > 0 } && Stash is null;
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
