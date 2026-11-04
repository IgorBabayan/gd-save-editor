using System.IO;
using GD.SaveEditor.Services.GameData.Database;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Services.Saves;

/// <summary>
/// Reads the header of <c>player.gdc</c>:
/// <code>
/// int32 seed, then encrypted: int32 magic ("GDCX"), int32 version (1 or 2),
/// wstring name, bool male, string classTag, int32 level, bool hardcore, [version 2+: byte expansionStatus]
/// </code>
/// The class tag is turned into a name with the game texts of <c>database.db</c>.
/// <para>
/// After the header come <c>int32 0</c> (key unchanged), <c>int32 dataVersion</c>, 16 bytes of character id, and the
/// blocks: <c>int32 id, int32 length (key unchanged), body, int32 0 (key unchanged)</c>. Block 2 ("bio") holds level,
/// experience, unspent points and attributes; block 3 (inventory) holds the bags and the equipment.
/// Problems in this part are reported in <see cref="CharacterSave.DetailsError"/> and never stop the character from
/// being listed.
/// </para>
/// </summary>
public sealed class CharacterSaveParser(IGameTextProvider gameTexts) : ICharacterSaveParser
{
    /// <summary>"GDCX" read as a little-endian int32.</summary>
    private const uint GdcMagic = 0x58434447;

    public CharacterSave Parse(string filePath)
    {
        var info = new FileInfo(filePath);
        var data = File.ReadAllBytes(filePath);
        if (data.Length < 16)
            throw new InvalidDataException("The file is too short to be a Grim Dawn character.");

        var reader = new GdcReader(data);
        if (reader.ReadUInt32() != GdcMagic)
            throw new InvalidDataException("Not a Grim Dawn character file (GDCX header missing).");

        var version = reader.ReadInt32();
        if (version < 1)
            throw new InvalidDataException($"Unsupported character file version {version}.");

        var name = reader.ReadWideString();
        var male = reader.ReadBool();
        var classTag = reader.ReadString();
        var level = reader.ReadInt32();
        var hardcore = reader.ReadBool();
        var expansionStatus = version >= 2 ? reader.ReadByte() : (byte)0;

        CharacterStats? stats = null;
        CharacterEquipment? equipment = null;
        string? detailsError = null;
        try
        {
            (stats, equipment) = ReadBody(reader);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or OverflowException)
        {
            detailsError = ex.Message;
        }

        return new CharacterSave
        {
            FilePath = info.FullName,
            CharacterName = string.IsNullOrWhiteSpace(name) ? Path.GetFileName(info.DirectoryName ?? filePath).TrimStart('_') : name,
            Level = level,
            ClassTag = classTag,
            ClassDisplayName = gameTexts.GetText(classTag),
            Hardcore = hardcore,
            IsMale = male,
            ExpansionStatus = expansionStatus,
            Stats = stats,
            Equipment = equipment,
            DetailsError = detailsError,
            LastPlayed = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            RawData = data,
            FileLength = info.Length,
            FileLastWriteUtc = info.LastWriteTimeUtc
        };
    }

    #region Body

    private const uint BioBlockId = 2;
    private const uint InventoryBlockId = 3;

    /// <summary>Armor and jewelry slots stored before the weapons, see <see cref="EquipmentSlot"/>.</summary>
    private const int ArmorSlotCount = 12;

    /// <summary>Reads the blocks after the header up to the inventory block.</summary>
    private static (CharacterStats? Stats, CharacterEquipment Equipment) ReadBody(GdcReader reader)
    {
        if (reader.ReadUInt32KeyUnchanged() != 0)
            throw new InvalidDataException("Unexpected data after the character header.");

        var dataVersion = reader.ReadInt32();
        if (dataVersion < 6)
            throw new InvalidDataException($"Character data version {dataVersion} is not supported.");

        reader.Skip(16); // character id

        CharacterStats? stats = null;
        while (reader.Position < reader.Length)
        {
            var (id, end) = reader.ReadBlockStart();
            switch (id)
            {
                case BioBlockId:
                    stats = TryReadBio(reader, end);
                    break;
                case InventoryBlockId:
                    var equipment = ReadInventory(reader, end);
                    reader.ReadBlockEnd(end);
                    return (stats, equipment);
                default:
                    reader.Skip(end - reader.Position);
                    break;
            }

            reader.ReadBlockEnd(end);
        }

        throw new InvalidDataException("The character file has no inventory block.");
    }

    /// <summary>
    /// <code>
    /// int32 version, int32 level, int32 experience, int32 attributePoints, int32 skillPoints,
    /// int32 devotionPoints, int32 totalDevotion, float physique, cunning, spirit, health, energy
    /// </code>
    /// Returns <c>null</c> (and leaves the reader at the block end) when the block has another layout.
    /// </summary>
    private static CharacterStats? TryReadBio(GdcReader reader, int end)
    {
        var start = reader.Snapshot();
        try
        {
            reader.ReadInt32(); // version
            var stats = new CharacterStats(
                Level: reader.ReadInt32(),
                Experience: reader.ReadUInt32(),
                AttributePoints: reader.ReadInt32(),
                SkillPoints: reader.ReadInt32(),
                DevotionPoints: reader.ReadInt32(),
                TotalDevotion: reader.ReadInt32(),
                Physique: reader.ReadSingle(),
                Cunning: reader.ReadSingle(),
                Spirit: reader.ReadSingle(),
                Health: reader.ReadSingle(),
                Energy: reader.ReadSingle());

            if (reader.Position == end)
                return stats;
        }
        catch (InvalidDataException)
        {
            // Read past the block: fall through and skip it.
        }

        reader.Restore(start);
        reader.Skip(end - reader.Position);
        return null;
    }

    /// <summary>
    /// <code>
    /// int32 version, byte hasItems; when set:
    ///   int32 bagCount, int32 focusedBag, int32 selectedBag, bagCount x bag block (id 0),
    ///   byte useAlternate, 12 x equipment item,
    ///   byte alternate1, 2 x equipment item (weapon set 1),
    ///   byte alternate2, 2 x equipment item (weapon set 2)
    /// equipment item = item, byte attached
    /// </code>
    /// </summary>
    private static CharacterEquipment ReadInventory(GdcReader reader, int end)
    {
        var version = reader.ReadInt32();
        if (!reader.ReadBool())
        {
            reader.Skip(end - reader.Position);
            return new CharacterEquipment([], 1);
        }

        var bagCount = reader.ReadInt32();
        if (bagCount is < 0 or > 64)
            throw new InvalidDataException($"Invalid number of inventory bags ({bagCount}, inventory version {version}).");
        reader.ReadInt32(); // focused bag
        reader.ReadInt32(); // selected bag

        for (var i = 0; i < bagCount; i++)
        {
            var (_, bagEnd) = reader.ReadBlockStart();
            reader.Skip(bagEnd - reader.Position);
            reader.ReadBlockEnd(bagEnd);
        }

        var items = new List<EquippedItem>();
        var useAlternate = reader.ReadBool();
        for (var i = 0; i < ArmorSlotCount; i++)
            AddEquipped(items, (EquipmentSlot)i, 0, reader);

        var set1IsAlternate = reader.ReadBool();
        AddEquipped(items, EquipmentSlot.MainHand, 1, reader);
        AddEquipped(items, EquipmentSlot.OffHand, 1, reader);

        var set2IsAlternate = reader.ReadBool();
        AddEquipped(items, EquipmentSlot.MainHand, 2, reader);
        AddEquipped(items, EquipmentSlot.OffHand, 2, reader);

        if (reader.Position != end)
            throw new InvalidDataException($"Inventory version {version} has a layout this editor does not know.");

        // The "alternate" flags mark which set is the alternate one; useAlternate says it is the one in hand.
        var alternateSet = set1IsAlternate && !set2IsAlternate ? 1 : 2;
        var activeSet = useAlternate ? alternateSet : 3 - alternateSet;
        return new CharacterEquipment(items, activeSet);
    }

    private static void AddEquipped(List<EquippedItem> items, EquipmentSlot slot, int weaponSet, GdcReader reader)
    {
        var item = ReadItem(reader);
        var attached = reader.ReadBool();
        if (!item.IsEmpty)
            items.Add(new EquippedItem(slot, weaponSet, item, attached));
    }

    /// <summary>
    /// <code>
    /// string base, prefix, suffix, modifier, transmute; int32 seed;
    /// string component, completionBonus; int32 componentSeed;
    /// string augment; int32 unknown, augmentSeed, var1, stackCount
    /// </code>
    /// </summary>
    private static SaveItem ReadItem(GdcReader reader) => new(
        BaseRecord: reader.ReadString(),
        PrefixRecord: reader.ReadString(),
        SuffixRecord: reader.ReadString(),
        ModifierRecord: reader.ReadString(),
        TransmuteRecord: reader.ReadString(),
        Seed: reader.ReadUInt32(),
        ComponentRecord: reader.ReadString(),
        CompletionBonusRecord: reader.ReadString(),
        ComponentSeed: reader.ReadUInt32(),
        AugmentRecord: reader.ReadString(),
        Unknown: reader.ReadUInt32(),
        AugmentSeed: reader.ReadUInt32(),
        Var1: reader.ReadUInt32(),
        StackCount: reader.ReadUInt32());

    #endregion
}
