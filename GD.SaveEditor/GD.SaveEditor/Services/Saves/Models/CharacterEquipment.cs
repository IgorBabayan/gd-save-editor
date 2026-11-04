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
    public bool IsEmpty => string.IsNullOrEmpty(BaseRecord);
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
