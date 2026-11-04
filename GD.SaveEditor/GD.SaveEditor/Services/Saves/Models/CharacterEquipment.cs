namespace GD.SaveEditor.Services.Saves.Models;

/// <summary>Equipment slots of a character, in the order of <c>player.gdc</c> (the 12 armor slots, then the weapons).</summary>
public enum EquipmentSlot
{
    Head,
    Amulet,
    Chest,
    Legs,
    Feet,
    Hands,
    Ring1,
    Ring2,
    Waist,
    Shoulders,
    Medal,
    Relic,
    MainHand,
    OffHand
}

/// <summary>An item as stored in <c>player.gdc</c>: record paths of the base item and everything added to it.</summary>
/// <param name="BaseRecord">Item record, e.g. <c>records/items/gearhead/a01_head001.dbr</c>; empty for an empty slot.</param>
/// <param name="PrefixRecord">Prefix affix record (empty when none).</param>
/// <param name="SuffixRecord">Suffix affix record (empty when none).</param>
/// <param name="ModifierRecord">Crafting modifier (blacksmith bonus) record.</param>
/// <param name="TransmuteRecord">Illusion (transmuted look) record.</param>
/// <param name="ComponentRecord">Component or relic slotted into the item.</param>
/// <param name="CompletionBonusRecord">Completion bonus of the slotted component / relic.</param>
/// <param name="AugmentRecord">Augment applied to the item.</param>
public sealed record SaveItem(
    string BaseRecord,
    string PrefixRecord,
    string SuffixRecord,
    string ModifierRecord,
    string TransmuteRecord,
    uint Seed,
    string ComponentRecord,
    string CompletionBonusRecord,
    uint ComponentSeed,
    string AugmentRecord,
    uint Unknown,
    uint AugmentSeed,
    uint Var1,
    uint StackCount)
{
    /// <summary>
    /// int32 fields newer inventory versions store after <see cref="StackCount"/> (four in version 11); kept so the
    /// item can be written back unchanged.
    /// </summary>
    public IReadOnlyList<uint> ExtraFields { get; init; } = [];

    public bool IsEmpty => string.IsNullOrEmpty(BaseRecord);

    /// <summary>
    /// How many items the stack holds. Version 11 inventories keep the count in the second extra field (1 for gear,
    /// 352 for a stack of dynamite) and leave <see cref="StackCount"/> at 0; older ones use <see cref="StackCount"/>.
    /// </summary>
    public uint Quantity => ExtraFields.Count >= 2 ? ExtraFields[1] : StackCount;

    /// <summary>
    /// A new item of <paramref name="baseRecord"/> with nothing added to it. The extra fields are left empty: the save
    /// editor fills them for the file's inventory version when it writes the item (see <see cref="WithExtraFieldCount"/>).
    /// </summary>
    public static SaveItem Create(string baseRecord, uint seed, uint quantity = 1) =>
        new(baseRecord, string.Empty, string.Empty, string.Empty, string.Empty, seed,
            string.Empty, string.Empty, 0, string.Empty, 0, 0, 0, Math.Max(1, quantity));

    /// <summary>
    /// The item holding <paramref name="quantity"/> items, stored where the item keeps its count: the second extra
    /// field of version 11 inventories, <see cref="StackCount"/> otherwise.
    /// </summary>
    public SaveItem WithQuantity(uint quantity)
    {
        quantity = Math.Max(1, quantity);
        if (ExtraFields.Count < 2)
            return this with { StackCount = quantity };

        var extra = ExtraFields.ToArray();
        extra[1] = quantity;
        return this with { ExtraFields = extra };
    }

    /// <summary>
    /// The item with exactly <paramref name="count"/> extra fields, as the inventory of the file stores them. An item
    /// made by the editor (no extra fields yet) moves its count from <see cref="StackCount"/> into the second field,
    /// like the game does in version 11; the other new fields are 0.
    /// </summary>
    public SaveItem WithExtraFieldCount(int count)
    {
        if (ExtraFields.Count == count)
            return this;

        var extra = new uint[count];
        for (var i = 0; i < Math.Min(count, ExtraFields.Count); i++)
            extra[i] = ExtraFields[i];

        if (ExtraFields.Count < 2 && count >= 2)
        {
            extra[1] = Math.Max(1, StackCount);
            return this with { ExtraFields = extra, StackCount = 0 };
        }

        if (ExtraFields.Count >= 2 && count < 2)
            return this with { ExtraFields = extra, StackCount = Math.Max(1, ExtraFields[1]) };

        return this with { ExtraFields = extra };
    }
}

/// <summary>An item in an equipment slot.</summary>
/// <param name="WeaponSet">1 or 2 for weapon slots (the two weapon sets), 0 for armor and jewelry.</param>
/// <param name="Attached">The game's "attached" flag of the slot.</param>
public sealed record EquippedItem(EquipmentSlot Slot, int WeaponSet, SaveItem Item, bool Attached);

/// <summary>What the character wears, read from the inventory block of <c>player.gdc</c>.</summary>
/// <param name="Items">Occupied slots only.</param>
/// <param name="ActiveWeaponSet">Weapon set (1 or 2) the character has in hand.</param>
public sealed record CharacterEquipment(IReadOnlyList<EquippedItem> Items, int ActiveWeaponSet)
{
    public SaveItem? Get(EquipmentSlot slot, int weaponSet = 0) =>
        Items.FirstOrDefault(i => i.Slot == slot && i.WeaponSet == weaponSet)?.Item;
}

/// <summary>An item in a bag, at its top-left cell.</summary>
public sealed record InventoryItem(SaveItem Item, int X, int Y);

/// <summary>One bag of the inventory with the items in it.</summary>
public sealed record InventoryBag(IReadOnlyList<InventoryItem> Items);

/// <summary>
/// The bags of the character, read from the inventory block of <c>player.gdc</c>: the main bag first, then the bags
/// picked from the tabs beside it.
/// </summary>
/// <param name="SelectedBag">Bag the game shows beside the main bag.</param>
public sealed record CharacterInventory(IReadOnlyList<InventoryBag> Bags, int FocusedBag, int SelectedBag);

/// <summary>Character progress from the "bio" block of <c>player.gdc</c>.</summary>
/// <param name="AttributePoints">Unspent attribute points.</param>
/// <param name="SkillPoints">Unspent skill points.</param>
/// <param name="DevotionPoints">Unspent devotion points.</param>
/// <param name="TotalDevotion">Devotion points earned in total.</param>
/// <param name="Health">Base health (without items and skills).</param>
/// <param name="Energy">Base energy (without items and skills).</param>
public sealed record CharacterStats(
    int Level,
    long Experience,
    int AttributePoints,
    int SkillPoints,
    int DevotionPoints,
    int TotalDevotion,
    float Physique,
    float Cunning,
    float Spirit,
    float Health,
    float Energy);
