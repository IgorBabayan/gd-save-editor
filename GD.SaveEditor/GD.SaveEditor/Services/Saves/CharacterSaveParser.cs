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
    /// Newer inventory versions add int32 fields at the end of every item (version 4: none, version 11: four).
    /// Since the number is not known for every version, the equipment is read with the count expected for the
    /// version first and then with the others; a count is accepted only when every slot holds a record path (or is
    /// empty) and the block ends exactly after the last weapon.
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

        var equipmentStart = reader.Snapshot();
        foreach (var extraFields in ExtraItemFieldCandidates(version))
        {
            reader.Restore(equipmentStart);
            try
            {
                if (TryReadEquipment(reader, end, extraFields) is { } equipment)
                    return equipment;
            }
            catch (InvalidDataException)
            {
                // Wrong field count: the next candidate is tried from the same position.
            }
        }

        throw new InvalidDataException($"Inventory version {version} has an item layout this editor does not know.");
    }

    /// <summary>Extra int32 fields per item to try, the one known for <paramref name="version"/> first.</summary>
    private static IEnumerable<int> ExtraItemFieldCandidates(int version)
    {
        var expected = version >= 11 ? 4 : 0;
        yield return expected;
        for (var count = 0; count <= 8; count++)
        {
            if (count != expected)
                yield return count;
        }
    }

    /// <summary>The equipment read with <paramref name="extraFields"/> extra fields per item, or <c>null</c> when that does not fit.</summary>
    private static CharacterEquipment? TryReadEquipment(GdcReader reader, int end, int extraFields)
    {
        var items = new List<EquippedItem>();
        var useAlternate = reader.ReadBool();
        for (var i = 0; i < ArmorSlotCount; i++)
        {
            if (!TryAddEquipped(items, (EquipmentSlot)i, 0, reader, extraFields))
                return null;
        }

        var set1IsAlternate = reader.ReadBool();
        if (!TryAddEquipped(items, EquipmentSlot.MainHand, 1, reader, extraFields)
            || !TryAddEquipped(items, EquipmentSlot.OffHand, 1, reader, extraFields))
            return null;

        var set2IsAlternate = reader.ReadBool();
        if (!TryAddEquipped(items, EquipmentSlot.MainHand, 2, reader, extraFields)
            || !TryAddEquipped(items, EquipmentSlot.OffHand, 2, reader, extraFields))
            return null;

        if (reader.Position != end)
            return null;

        // The "alternate" flags mark which set is the alternate one; useAlternate says it is the one in hand.
        var alternateSet = set1IsAlternate && !set2IsAlternate ? 1 : 2;
        var activeSet = useAlternate ? alternateSet : 3 - alternateSet;
        return new CharacterEquipment(items, activeSet);
    }

    /// <summary>Reads one slot; <c>false</c> when it does not look like an item (the field count is wrong).</summary>
    private static bool TryAddEquipped(List<EquippedItem> items, EquipmentSlot slot, int weaponSet, GdcReader reader, int extraFields)
    {
        var item = ReadItem(reader, extraFields);
        var attached = reader.ReadBool();
        if (!item.IsEmpty && !item.BaseRecord.StartsWith("records/", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!item.IsEmpty)
            items.Add(new EquippedItem(slot, weaponSet, item, attached));
        return true;
    }

    /// <summary>
    /// <code>
    /// string base, prefix, suffix, modifier, transmute; int32 seed;
    /// string component, completionBonus; int32 componentSeed;
    /// string augment; int32 unknown, augmentSeed, var1, stackCount,
    /// [newer inventory versions: <paramref name="extraFields"/> x int32]
    /// </code>
    /// </summary>
    private static SaveItem ReadItem(GdcReader reader, int extraFields)
    {
        var item = new SaveItem(
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

        if (extraFields == 0)
            return item;

        var extra = new uint[extraFields];
        for (var i = 0; i < extra.Length; i++)
            extra[i] = reader.ReadUInt32();
        return item with { ExtraFields = extra };
    }

    #endregion
}
