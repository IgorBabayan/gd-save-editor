using System.IO;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Services.Saves;

/// <summary>
/// Everything the editor reads from a <c>player.gdc</c>:
/// <code>
/// int32 seed, then encrypted: int32 magic ("GDCX"), int32 version (1 or 2),
/// wstring name, bool male, string classTag, int32 level, bool hardcore, [version 2+: byte expansionStatus]
/// </code>
/// then <c>int32 0</c> (key unchanged), <c>int32 dataVersion</c>, 16 bytes of character id, and the blocks:
/// <c>int32 id, int32 length (key unchanged), body, int32 0 (key unchanged)</c>.
/// <para>
/// Block 1 holds the iron bits, block 2 ("bio") level, experience, unspent points and attributes, block 3 the bags
/// and the equipment, block 4 the personal stash, block 6 the unlocked waypoints, block 13 the reputation with every
/// faction. The other blocks after them (skills, UI, statistics, ...) are read value by
/// value as well, only so the file can be encrypted again after a change (see <see cref="GdcWriter"/>): every value
/// has to be read with its exact size.
/// </para>
/// <para>
/// A problem up to the inventory is reported in <see cref="DetailsError"/>; a block after it with a layout the editor
/// does not know only makes the file read-only (<see cref="RewriteError"/>). Neither stops the character from being
/// listed.
/// </para>
/// </summary>
public sealed class CharacterFile
{
    /// <summary>"GDCX" read as a little-endian int32.</summary>
    private const uint GdcMagic = 0x58434447;

    private const uint InfoBlockId = 1;
    private const uint BioBlockId = 2;
    private const uint InventoryBlockId = 3;
    private const uint StashBlockId = 4;
    private const uint RespawnBlockId = 5;
    private const uint TeleportBlockId = 6;
    private const uint MarkerBlockId = 7;
    private const uint SkillBlockId = 8;
    private const uint TokenBlockId = 10;
    private const uint NoteBlockId = 12;
    private const uint FactionBlockId = 13;
    private const uint UiBlockId = 14;
    private const uint TutorialBlockId = 15;
    private const uint PlayStatsBlockId = 16;
    private const uint ShrineBlockId = 17;

    /// <summary>Armor and jewelry slots stored before the weapons, see <see cref="EquipmentSlot"/>.</summary>
    private const int ArmorSlotCount = 12;

    /// <summary>Upper bound of list lengths, so a misread count fails fast instead of reading the whole file.</summary>
    private const int MaxCount = 100_000;

    /// <summary>Extra int32 fields per item found in the inventory, used for the stash items as well.</summary>
    private int _itemExtraFields;

    private CharacterFile()
    {
    }

    public string Name { get; private set; } = string.Empty;
    public bool IsMale { get; private set; }
    public string ClassTag { get; private set; } = string.Empty;
    public int Level { get; private set; }
    public bool Hardcore { get; private set; }
    public byte ExpansionStatus { get; private set; }

    public CharacterStats? Stats { get; private set; }
    public CharacterEquipment? Equipment { get; private set; }
    public CharacterInventory? Inventory { get; private set; }

    /// <summary>Unlocked waypoints (block 6) per difficulty; <c>null</c> when the block could not be read.</summary>
    public CharacterWaypoints? Waypoints { get; private set; }

    /// <summary>Reputation with every faction (block 13); <c>null</c> when the block could not be read.</summary>
    public CharacterFactions? Factions { get; private set; }

    /// <summary>The personal stash (block 4); <c>null</c> when the block could not be read.</summary>
    public CharacterStash? Stash { get; private set; }

    /// <summary>
    /// File offsets of the values of every faction, in the order of <see cref="Factions"/>, to change them in place
    /// (see <see cref="CharacterSaveEditor"/>); <c>null</c> when the block could not be read.
    /// </summary>
    public IReadOnlyList<FactionValueOffsets>? FactionOffsets { get; private set; }

    /// <summary>Iron bits; <c>null</c> when block 1 could not be read.</summary>
    public uint? Money { get; private set; }

    /// <summary>File offset of the iron bits value, to change it (see <see cref="GdcWriter"/>).</summary>
    public int? MoneyOffset { get; private set; }

    /// <summary>File offset of the name's length; the name's characters follow it, two bytes each.</summary>
    public int NameOffset { get; private set; }

    /// <summary>File offset of the "male" flag of the header.</summary>
    public int IsMaleOffset { get; private set; }

    /// <summary>Difficulty the character plays (character info); <c>null</c> when block 1 could not be read.</summary>
    public byte? Difficulty { get; private set; }

    public int? DifficultyOffset { get; private set; }

    /// <summary>Highest difficulty unlocked (character info); <c>null</c> when block 1 could not be read.</summary>
    public byte? GreatestDifficulty { get; private set; }

    public int? GreatestDifficultyOffset { get; private set; }

    /// <summary>Crucible tributes (character info version 4+); <c>null</c> when block 1 could not be read or has none.</summary>
    public uint? Tributes { get; private set; }

    public int? TributesOffset { get; private set; }

    /// <summary>
    /// File offset of the level in the bio block, the first of its 11 values (see <see cref="ReadBio"/>);
    /// <c>null</c> when the block could not be read.
    /// </summary>
    public int? StatsOffset { get; private set; }

    /// <summary>Why stats, equipment or bags could not be read; <c>null</c> when they were.</summary>
    public string? DetailsError { get; private set; }

    /// <summary>Why the file cannot be encrypted again with changes; <c>null</c> when every value was read.</summary>
    public string? RewriteError { get; private set; }

    /// <summary>
    /// int32 fields the file stores after the stack count of every item (four in version 11 inventories, none in older
    /// ones); an item written into the file needs exactly that many (see <see cref="SaveItem.WithExtraFieldCount"/>).
    /// </summary>
    public int ItemExtraFields => _itemExtraFields;

    /// <summary>Every value of the file in order; empty unless read with <c>recordTokens</c>.</summary>
    public IReadOnlyList<GdcToken> Tokens { get; private set; } = [];

    /// <summary>
    /// Where the bags and their items are in <see cref="Tokens"/>, to write the items back in other places;
    /// <c>null</c> unless read with <c>recordTokens</c> and the inventory has items.
    /// </summary>
    public InventoryTokenLayout? InventoryLayout { get; private set; }

    /// <summary>
    /// Where the 16 equipment slots are in <see cref="Tokens"/>, to write other items into them; <c>null</c> unless read
    /// with <c>recordTokens</c> and the inventory has items.
    /// </summary>
    public EquipmentTokenLayout? EquipmentLayout { get; private set; }

    /// <summary>
    /// Where the waypoint lists (block 6) are in <see cref="Tokens"/>, to write other lists; <c>null</c> unless read with
    /// <c>recordTokens</c>.
    /// </summary>
    public WaypointTokenLayout? WaypointLayout { get; private set; }

    /// <summary>
    /// Where the personal stash tabs (block 4) are in <see cref="Tokens"/>, to write the items back in other places;
    /// <c>null</c> unless read with <c>recordTokens</c>.
    /// </summary>
    public StashTokenLayout? StashLayout { get; private set; }

    private int _stashLengthToken = -1;
    private int _stashEndToken = -1;
    private List<StashTabTokenLayout>? _stashTabs;

    private int _teleportLengthToken = -1;
    private int _teleportEndToken = -1;
    private List<UidListTokenLayout>? _teleportLists;

    private int _inventoryLengthToken = -1;
    private int _inventoryEndToken = -1;
    private List<BagTokenLayout>? _bagLayouts;
    private List<SlotTokenLayout>? _slotLayouts;

    /// <summary>Reads a character file.</summary>
    /// <param name="recordTokens">Keep every value (<see cref="Tokens"/>) to encrypt the file again.</param>
    /// <exception cref="InvalidDataException">The header is missing or damaged: not a Grim Dawn character.</exception>
    public static CharacterFile Read(byte[] data, bool recordTokens = false)
    {
        if (data.Length < 16)
            throw new InvalidDataException("The file is too short to be a Grim Dawn character.");

        var reader = new GdcReader(data, recordTokens);
        if (reader.ReadUInt32() != GdcMagic)
            throw new InvalidDataException("Not a Grim Dawn character file (GDCX header missing).");

        var version = reader.ReadInt32();
        if (version < 1)
            throw new InvalidDataException($"Unsupported character file version {version}.");

        var file = new CharacterFile { NameOffset = reader.Position };
        file.Name = reader.ReadWideString();
        file.IsMaleOffset = reader.Position;
        file.IsMale = reader.ReadBool();
        file.ClassTag = reader.ReadString();
        file.Level = reader.ReadInt32();
        file.Hardcore = reader.ReadBool();
        file.ExpansionStatus = version >= 2 ? reader.ReadByte() : (byte)0;

        try
        {
            file.ReadBody(reader);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or OverflowException)
        {
            // Up to the inventory the details are lost; after it only writing is.
            if (file.Inventory is null)
            {
                file.DetailsError = ex.Message;
                file.Equipment = null;
            }

            file.RewriteError ??= ex.Message;
        }

        file.Tokens = reader.Tokens;
        if (recordTokens && file.RewriteError is null && file._bagLayouts is { Count: > 0 } bags
            && file._inventoryLengthToken >= 0 && file._inventoryEndToken >= 0)
            file.InventoryLayout = new InventoryTokenLayout(file._inventoryLengthToken, file._inventoryEndToken, bags);
        if (recordTokens && file.RewriteError is null && file._slotLayouts is { Count: > 0 } slots
            && file._inventoryLengthToken >= 0 && file._inventoryEndToken >= 0)
            file.EquipmentLayout = new EquipmentTokenLayout(file._inventoryLengthToken, file._inventoryEndToken, slots);
        if (recordTokens && file.RewriteError is null && file._teleportLists is { Count: > 0 } lists
            && file._teleportLengthToken >= 0 && file._teleportEndToken >= 0)
            file.WaypointLayout = new WaypointTokenLayout(file._teleportLengthToken, file._teleportEndToken, lists);
        if (recordTokens && file.RewriteError is null && file._stashTabs is { } tabs
            && file._stashLengthToken >= 0 && file._stashEndToken >= 0)
            file.StashLayout = new StashTokenLayout(file._stashLengthToken, file._stashEndToken, tabs);
        return file;
    }

    private void ReadBody(GdcReader reader)
    {
        if (reader.ReadUInt32KeyUnchanged() != 0)
            throw new InvalidDataException("Unexpected data after the character header.");

        var dataVersion = reader.ReadInt32();
        if (dataVersion < 6)
            throw new InvalidDataException($"Character data version {dataVersion} is not supported.");

        reader.Skip(16); // character id (16 byte values)

        while (reader.Position < reader.Length)
        {
            var blockToken = reader.TokenCount;
            var (id, end) = reader.ReadBlockStart();
            if (Inventory is null)
            {
                ReadBlockBeforeInventory(reader, id, end);
                if (id == InventoryBlockId)
                    (_inventoryLengthToken, _inventoryEndToken) = (blockToken + 1, reader.TokenCount);
            }
            else
            {
                ReadBlockAfterInventory(reader, id, end);
                if (id == TeleportBlockId && _teleportLists is not null)
                    (_teleportLengthToken, _teleportEndToken) = (blockToken + 1, reader.TokenCount);
                if (id == StashBlockId && _stashTabs is not null)
                    (_stashLengthToken, _stashEndToken) = (blockToken + 1, reader.TokenCount);
            }

            reader.ReadBlockEnd(end);
        }

        if (Inventory is null)
            throw new InvalidDataException("The character file has no inventory block.");
    }

    /// <summary>
    /// Blocks up to the inventory. One the editor cannot read is skipped (the next blocks are still read, as before),
    /// which only makes the file read-only.
    /// </summary>
    private void ReadBlockBeforeInventory(GdcReader reader, uint id, int end)
    {
        switch (id)
        {
            case InfoBlockId:
                TryReadBlock(reader, end, "character info", ReadInfo);
                break;
            case BioBlockId:
                TryReadBlock(reader, end, "attributes", ReadBio);
                break;
            case InventoryBlockId:
                ReadInventory(reader, end);
                break;
            default:
                SkipBlock(reader, end, $"block {id} before the inventory");
                break;
        }
    }

    /// <summary>Blocks after the inventory: read value by value only so the file can be encrypted again.</summary>
    private void ReadBlockAfterInventory(GdcReader reader, uint id, int end)
    {
        var version = reader.ReadInt32();
        switch (id)
        {
            case StashBlockId when version >= 6:
                ReadStash(reader);
                break;
            case RespawnBlockId when version == 1:
                ReadUidLists(reader, 3);
                for (var i = 0; i < 3; i++)
                    reader.Skip(16);
                break;
            case TeleportBlockId when version == 1:
                ReadWaypoints(reader);
                break;
            case MarkerBlockId when version == 1:
                ReadUidLists(reader, 3);
                break;
            case ShrineBlockId when version == 2:
                ReadUidLists(reader, 6);
                break;
            case SkillBlockId when version is >= 5 and <= 8:
                ReadSkills(reader, version);
                break;
            case TokenBlockId when version == 2:
                for (var i = 0; i < 3; i++)
                    ReadStrings(reader);
                break;
            case NoteBlockId when version == 1:
                ReadStrings(reader);
                break;
            case FactionBlockId when version == 5:
                ReadFactions(reader);
                break;
            case UiBlockId when version is 4 or 5 or 7:
                ReadUi(reader, version);
                break;
            case TutorialBlockId when version == 1:
                var pages = ReadCount(reader);
                for (var i = 0; i < pages; i++)
                    reader.ReadUInt32();
                break;
            case PlayStatsBlockId when version == 12:
                ReadPlayStats(reader);
                break;
            default:
                throw new InvalidDataException($"Block {id} version {version} has a layout this editor does not know.");
        }

        if (reader.Position != end)
            throw new InvalidDataException($"Block {id} version {version} has a layout this editor does not know.");
    }

    /// <summary>Reads a block with <paramref name="read"/>; when its layout does not fit, skips it and marks the file read-only.</summary>
    private void TryReadBlock(GdcReader reader, int end, string name, Action<GdcReader, int> read)
    {
        var start = reader.Snapshot();
        try
        {
            read(reader, end);
            if (reader.Position == end)
                return;
        }
        catch (InvalidDataException)
        {
            // Read past the block: fall through and skip it.
        }

        reader.Restore(start);
        SkipBlock(reader, end, $"the {name} block");
    }

    private void SkipBlock(GdcReader reader, int end, string what)
    {
        RewriteError ??= $"The save file has {what} with a layout this editor does not know.";
        reader.Skip(end - reader.Position);
    }

    #region Character info, bio

    /// <summary>
    /// <code>
    /// int32 version (3-5), bool inMainQuest, bool hasBeenInGame, byte difficulty, byte greatestDifficulty,
    /// int32 money, [version 4+: byte greatestSurvivalDifficulty, int32 tribute], byte compassState,
    /// byte skillWindowShowHelp, bool weaponSwapActive, bool weaponSwapEnabled, string texture,
    /// [version 5+: int32 count, count x byte lootFilter]
    /// </code>
    /// </summary>
    private void ReadInfo(GdcReader reader, int end)
    {
        var version = reader.ReadInt32();
        if (version is < 3 or > 5)
            throw new InvalidDataException($"Character info version {version} is not supported.");

        reader.ReadBool();
        reader.ReadBool();
        var difficultyOffset = reader.Position;
        var difficulty = reader.ReadByte();
        var greatestOffset = reader.Position;
        var greatest = reader.ReadByte();
        var moneyOffset = reader.Position;
        var money = reader.ReadUInt32();
        int? tributesOffset = null;
        uint? tributes = null;
        if (version >= 4)
        {
            reader.ReadByte();
            tributesOffset = reader.Position;
            tributes = reader.ReadUInt32();
        }

        reader.ReadByte();
        reader.ReadByte();
        reader.ReadBool();
        reader.ReadBool();
        reader.ReadString();
        if (version >= 5)
        {
            var filters = ReadCount(reader);
            for (var i = 0; i < filters; i++)
                reader.ReadByte();
        }

        if (reader.Position != end)
            throw new InvalidDataException("Character info block has an unknown layout.");

        (MoneyOffset, Money) = (moneyOffset, money);
        (DifficultyOffset, Difficulty) = (difficultyOffset, difficulty);
        (GreatestDifficultyOffset, GreatestDifficulty) = (greatestOffset, greatest);
        (TributesOffset, Tributes) = (tributesOffset, tributes);
    }

    /// <summary>
    /// <code>
    /// int32 version, int32 level, int32 experience, int32 attributePoints, int32 skillPoints,
    /// int32 devotionPoints, int32 totalDevotion, float physique, cunning, spirit, health, energy
    /// </code>
    /// </summary>
    private void ReadBio(GdcReader reader, int end)
    {
        reader.ReadInt32(); // version
        var statsOffset = reader.Position;
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
            (Stats, StatsOffset) = (stats, statsOffset);
    }

    #endregion

    #region Inventory

    /// <summary>
    /// <code>
    /// int32 version, byte hasItems; when set:
    ///   int32 bagCount, int32 focusedBag, int32 selectedBag,
    ///   bagCount x bag block (id 0: byte, int32 count, count x (item, int32 x, int32 y)),
    ///   byte useAlternate, 12 x equipment item,
    ///   byte alternate1, 2 x equipment item (weapon set 1),
    ///   byte alternate2, 2 x equipment item (weapon set 2)
    /// equipment item = item, byte attached
    /// </code>
    /// Newer inventory versions add int32 fields at the end of every item (version 4: none, version 11: four).
    /// Since the number is not known for every version, the bags and the equipment are read with the count expected
    /// for the version first and then with the others; a count is accepted only when every item has a record path,
    /// every bag ends where its block says and the block ends exactly after the last weapon.
    /// </summary>
    private void ReadInventory(GdcReader reader, int end)
    {
        var version = reader.ReadInt32();
        if (!reader.ReadBool())
        {
            if (reader.Position != end)
                SkipBlock(reader, end, "an inventory without items");
            Equipment = new CharacterEquipment([], 1);
            Inventory = new CharacterInventory([], 0, 0);
            _itemExtraFields = ExtraItemFieldCandidates(version).First();
            return;
        }

        var bagCount = reader.ReadInt32();
        if (bagCount is < 0 or > 64)
            throw new InvalidDataException($"Invalid number of inventory bags ({bagCount}, inventory version {version}).");
        var focusedBag = reader.ReadInt32();
        var selectedBag = reader.ReadInt32();

        var itemsStart = reader.Snapshot();
        foreach (var extraFields in ExtraItemFieldCandidates(version))
        {
            reader.Restore(itemsStart);
            try
            {
                var bags = new List<InventoryBag>(bagCount);
                var layouts = new List<BagTokenLayout>(bagCount);
                for (var i = 0; i < bagCount; i++)
                {
                    var (bag, layout) = ReadBag(reader, extraFields);
                    bags.Add(bag);
                    layouts.Add(layout);
                }

                var slotLayouts = new List<SlotTokenLayout>(ArmorSlotCount + 4);
                if (TryReadEquipment(reader, end, extraFields, slotLayouts) is { } equipment)
                {
                    _bagLayouts = layouts;
                    _slotLayouts = slotLayouts;
                    _itemExtraFields = extraFields;
                    Equipment = equipment;
                    Inventory = new CharacterInventory(bags, focusedBag, selectedBag);
                    return;
                }
            }
            catch (InvalidDataException)
            {
                // Wrong field count: the next candidate is tried from the same position.
            }
        }

        throw new InvalidDataException($"Inventory version {version} has an item layout this editor does not know.");
    }

    /// <summary>Extra int32 fields per item to try, the one known for <paramref name="version"/> first.</summary>
    internal static IEnumerable<int> ExtraItemFieldCandidates(int version)
    {
        var expected = version >= 11 ? 4 : 0;
        yield return expected;
        for (var count = 0; count <= 8; count++)
        {
            if (count != expected)
                yield return count;
        }
    }

    /// <summary>A bag and where its values are in the recorded tokens (indices are only meaningful when recording).</summary>
    private static (InventoryBag Bag, BagTokenLayout Layout) ReadBag(GdcReader reader, int extraFields)
    {
        var start = reader.TokenCount;
        var (id, end) = reader.ReadBlockStart();
        if (id != 0)
            throw new InvalidDataException($"Unexpected block {id} in the inventory.");

        reader.ReadBool();
        var countToken = reader.TokenCount;
        var count = ReadCount(reader);
        var items = new List<InventoryItem>(count);
        var itemLayouts = new List<ItemTokenLayout>(count);
        for (var i = 0; i < count; i++)
        {
            var itemStart = reader.TokenCount;
            var item = ReadItem(reader, extraFields);
            var xToken = reader.TokenCount;
            var x = reader.ReadInt32();
            var yToken = reader.TokenCount;
            var y = reader.ReadInt32();
            if (!IsItemRecord(item) || x is < 0 or > 1000 || y is < 0 or > 1000)
                throw new InvalidDataException("A bag item does not fit the item layout.");
            items.Add(new InventoryItem(item, x, y));
            itemLayouts.Add(new ItemTokenLayout(itemStart, reader.TokenCount, xToken, yToken));
        }

        var endToken = reader.TokenCount;
        reader.ReadBlockEnd(end);
        return (new InventoryBag(items), new BagTokenLayout(start, countToken, endToken, itemLayouts));
    }

    /// <summary>The equipment read with <paramref name="extraFields"/> extra fields per item, or <c>null</c> when that does not fit.</summary>
    /// <remarks>Where every slot's values are (indices only meaningful when recording) goes into <paramref name="layouts"/>.</remarks>
    private static CharacterEquipment? TryReadEquipment(GdcReader reader, int end, int extraFields, List<SlotTokenLayout> layouts)
    {
        var items = new List<EquippedItem>();
        var useAlternate = reader.ReadBool();
        for (var i = 0; i < ArmorSlotCount; i++)
        {
            if (!TryAddEquipped(items, layouts, (EquipmentSlot)i, 0, reader, extraFields))
                return null;
        }

        var set1IsAlternate = reader.ReadBool();
        if (!TryAddEquipped(items, layouts, EquipmentSlot.MainHand, 1, reader, extraFields)
            || !TryAddEquipped(items, layouts, EquipmentSlot.OffHand, 1, reader, extraFields))
            return null;

        var set2IsAlternate = reader.ReadBool();
        if (!TryAddEquipped(items, layouts, EquipmentSlot.MainHand, 2, reader, extraFields)
            || !TryAddEquipped(items, layouts, EquipmentSlot.OffHand, 2, reader, extraFields))
            return null;

        if (reader.Position != end)
            return null;

        // The "alternate" flags mark which set is the alternate one; useAlternate says it is the one in hand.
        var alternateSet = set1IsAlternate && !set2IsAlternate ? 1 : 2;
        var activeSet = useAlternate ? alternateSet : 3 - alternateSet;
        return new CharacterEquipment(items, activeSet);
    }

    /// <summary>Reads one slot; <c>false</c> when it does not look like an item (the field count is wrong).</summary>
    private static bool TryAddEquipped(
        List<EquippedItem> items, List<SlotTokenLayout> layouts, EquipmentSlot slot, int weaponSet, GdcReader reader, int extraFields)
    {
        var start = reader.TokenCount;
        var item = ReadItem(reader, extraFields);
        var attachedToken = reader.TokenCount;
        var attached = reader.ReadBool();
        layouts.Add(new SlotTokenLayout(slot, weaponSet, start, attachedToken, attachedToken));
        if (!item.IsEmpty && !IsItemRecord(item))
            return false;

        if (!item.IsEmpty)
            items.Add(new EquippedItem(slot, weaponSet, item, attached));
        return true;
    }

    internal static bool IsItemRecord(SaveItem item) =>
        item.BaseRecord.StartsWith("records/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <code>
    /// string base, prefix, suffix, modifier, transmute; int32 seed;
    /// string component, completionBonus; int32 componentSeed;
    /// string augment; int32 unknown, augmentSeed, var1, stackCount,
    /// [newer inventory versions: <paramref name="extraFields"/> x int32]
    /// </code>
    /// </summary>
    internal static SaveItem ReadItem(GdcReader reader, int extraFields)
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

    #region Blocks after the inventory

    /// <summary>
    /// Character stash, version 6+: <c>int32 tabCount</c>, then a tab (see <see cref="ReadStashTab"/>) per tab the
    /// character bought.
    /// </summary>
    private void ReadStash(GdcReader reader)
    {
        var count = ReadCount(reader);
        var tabs = new List<StashTab>(count);
        var layouts = new List<StashTabTokenLayout>(count);
        for (var i = 0; i < count; i++)
        {
            var (tab, layout) = ReadStashTab(reader, _itemExtraFields);
            tabs.Add(tab);
            layouts.Add(layout);
        }

        Stash = new CharacterStash(tabs);
        _stashTabs = layouts;
    }

    /// <summary>
    /// A stash tab, in the character stash and in the transfer stash: a block (id 0) with
    /// <c>int32 width, int32 height, int32 count, count x (item, float x, float y)</c>, then int32 fields that are zero
    /// in every known save (five in version 11). The zeros are kept as they are when the tab is written again.
    /// </summary>
    internal static (StashTab Tab, StashTabTokenLayout Layout) ReadStashTab(GdcReader reader, int extraFields)
    {
        var start = reader.TokenCount;
        var (id, end) = reader.ReadBlockStart();
        if (id != 0)
            throw new InvalidDataException($"Unexpected block {id} in the stash.");

        var width = reader.ReadInt32();
        var height = reader.ReadInt32();
        if (width is < 1 or > 100 || height is < 1 or > 100)
            throw new InvalidDataException($"A stash tab has an invalid size ({width} x {height}).");

        var countToken = reader.TokenCount;
        var count = ReadCount(reader);
        var items = new List<StashItem>(count);
        var itemLayouts = new List<ItemTokenLayout>(count);
        for (var i = 0; i < count; i++)
        {
            var itemStart = reader.TokenCount;
            var item = ReadItem(reader, extraFields);
            var xToken = reader.TokenCount;
            var x = reader.ReadSingle();
            var yToken = reader.TokenCount;
            var y = reader.ReadSingle();
            if (!IsItemRecord(item) || !IsCell(x) || !IsCell(y))
                throw new InvalidDataException("A stash item does not fit the item layout.");
            items.Add(new StashItem(item, x, y));
            itemLayouts.Add(new ItemTokenLayout(itemStart, reader.TokenCount, xToken, yToken));
        }

        var itemsEnd = reader.TokenCount;
        while (reader.Position < end)
        {
            if (end - reader.Position < 4 || reader.ReadUInt32() != 0)
                throw new InvalidDataException("The stash has data this editor does not know.");
        }

        var endToken = reader.TokenCount;
        reader.ReadBlockEnd(end);
        return (new StashTab(width, height, items), new StashTabTokenLayout(start, countToken, itemsEnd, endToken, itemLayouts));
    }

    private static bool IsCell(float value) => float.IsFinite(value) && value is >= 0 and <= 1000;

    /// <summary>
    /// Waypoints (block 6, version 1): for Normal, Elite and Ultimate <c>int32 count, count x 16-byte id</c> of the
    /// rift gates unlocked in that difficulty (see <c>GameWaypoint.Uid</c>).
    /// </summary>
    private void ReadWaypoints(GdcReader reader)
    {
        var lists = new List<IReadOnlyList<string>>(CharacterWaypoints.DifficultyCount);
        var layouts = new List<UidListTokenLayout>(CharacterWaypoints.DifficultyCount);
        Span<byte> uid = stackalloc byte[16];
        for (var list = 0; list < CharacterWaypoints.DifficultyCount; list++)
        {
            var countToken = reader.TokenCount;
            var count = ReadCount(reader);
            var uids = new List<string>(count);
            for (var i = 0; i < count; i++)
            {
                for (var b = 0; b < uid.Length; b++)
                    uid[b] = reader.ReadByte();
                uids.Add(Convert.ToHexStringLower(uid));
            }

            lists.Add(uids);
            layouts.Add(new UidListTokenLayout(countToken, reader.TokenCount));
        }

        Waypoints = new CharacterWaypoints(lists);
        _teleportLists = layouts;
    }

    /// <summary><paramref name="lists"/> x (int32 count, count x 16-byte id): respawn points, waypoints, markers, shrines.</summary>
    private static void ReadUidLists(GdcReader reader, int lists)
    {
        for (var list = 0; list < lists; list++)
        {
            var count = ReadCount(reader);
            for (var i = 0; i < count; i++)
                reader.Skip(16);
        }
    }

    /// <summary>
    /// <code>
    /// int32 count, count x (string name, int32 level, bool enabled, [version 8: byte], int32 devotionLevel,
    ///   int32 experience, int32 active, byte, byte, string autoCastSkill, string autoCastController),
    /// int32 masteriesAllowed, int32 skillReclamationPointsUsed, int32 devotionReclamationPointsUsed,
    /// int32 count, count x (string name, string autoCastSkill, string autoCastController, int32 itemSlot, string itemName),
    /// [version 8: int32, zero in every known save]
    /// </code>
    /// </summary>
    private static void ReadSkills(GdcReader reader, int version)
    {
        var skills = ReadCount(reader);
        for (var i = 0; i < skills; i++)
        {
            reader.ReadString();
            reader.ReadUInt32();
            reader.ReadBool();
            if (version >= 8)
                reader.ReadByte();
            reader.ReadUInt32();
            reader.ReadUInt32();
            reader.ReadUInt32();
            reader.ReadByte();
            reader.ReadByte();
            reader.ReadString();
            reader.ReadString();
        }

        reader.ReadUInt32();
        reader.ReadUInt32();
        reader.ReadUInt32();

        var itemSkills = ReadCount(reader);
        for (var i = 0; i < itemSkills; i++)
        {
            reader.ReadString();
            reader.ReadString();
            reader.ReadString();
            reader.ReadUInt32();
            reader.ReadString();
        }

        if (version >= 8 && reader.ReadUInt32() != 0)
            throw new InvalidDataException("The skills have data this editor does not know.");
    }

    /// <summary>
    /// Factions (block 13, version 5):
    /// <c>int32 faction, int32 count, count x (bool modified, bool unlocked, float value, float positiveBoost, float negativeBoost)</c>.
    /// Every entry has the same size, so a value can be changed in place.
    /// </summary>
    private void ReadFactions(GdcReader reader)
    {
        reader.ReadUInt32();
        var count = ReadCount(reader);
        var factions = new List<FactionStanding>(count);
        var offsets = new List<FactionValueOffsets>(count);
        for (var i = 0; i < count; i++)
        {
            var modifiedOffset = reader.Position;
            var modified = reader.ReadBool();
            var unlockedOffset = reader.Position;
            var unlocked = reader.ReadBool();
            var valueOffset = reader.Position;
            var value = reader.ReadSingle();
            var positiveBoost = reader.ReadSingle();
            var negativeBoost = reader.ReadSingle();
            factions.Add(new FactionStanding(i, modified, unlocked, value, positiveBoost, negativeBoost));
            offsets.Add(new FactionValueOffsets(modifiedOffset, unlockedOffset, valueOffset));
        }

        Factions = new CharacterFactions(factions);
        FactionOffsets = offsets;
    }

    /// <summary>
    /// <code>
    /// byte, int32, byte, 5 x (string, string, byte),
    /// hot slots (version 4: 36, version 5: 46, version 7: int32, int32 count, int32, then count),
    /// float cameraDistance
    /// hot slot = int32 type; type 0: string skill, bool isItemSkill, string item, int32 equipLocation;
    ///   type 4: string item, string bitmapUp, string bitmapDown, wstring label; other types: nothing
    /// </code>
    /// </summary>
    private static void ReadUi(GdcReader reader, int version)
    {
        reader.ReadByte();
        reader.ReadUInt32();
        reader.ReadByte();
        for (var i = 0; i < 5; i++)
        {
            reader.ReadString();
            reader.ReadString();
            reader.ReadByte();
        }

        int slots;
        if (version >= 7)
        {
            reader.ReadUInt32();
            slots = ReadCount(reader);
            reader.ReadUInt32();
        }
        else
        {
            slots = version >= 5 ? 46 : 36;
        }

        for (var i = 0; i < slots; i++)
        {
            switch (reader.ReadUInt32())
            {
                case 0:
                    reader.ReadString();
                    reader.ReadBool();
                    reader.ReadString();
                    reader.ReadUInt32();
                    break;
                case 4:
                    reader.ReadString();
                    reader.ReadString();
                    reader.ReadString();
                    reader.ReadWideString();
                    break;
            }
        }

        reader.ReadSingle();
    }

    /// <summary>
    /// Version 12:
    /// <code>
    /// 11 x int32 (play time, max level, deaths, kills, ...), float greatestDamageInflicted,
    /// 3 x (string greatestMonster, int32 level, int32 lifeAndMana, string lastMonsterHit, string lastMonsterHitBy),
    /// int32 championKills, float lastHit, float lastHitBy, float greatestDamageReceived,
    /// 18 x int32 (hero kills, crafting, shrines, chests, notes, boss kills, survival, ...), byte, 4 x int32
    /// </code>
    /// </summary>
    private static void ReadPlayStats(GdcReader reader)
    {
        for (var i = 0; i < 11; i++)
            reader.ReadUInt32();
        reader.ReadSingle();

        for (var i = 0; i < 3; i++)
        {
            reader.ReadString();
            reader.ReadUInt32();
            reader.ReadUInt32();
            reader.ReadString();
            reader.ReadString();
        }

        reader.ReadUInt32();
        reader.ReadSingle();
        reader.ReadSingle();
        reader.ReadSingle();
        for (var i = 0; i < 18; i++)
            reader.ReadUInt32();
        reader.ReadByte();
        for (var i = 0; i < 4; i++)
            reader.ReadUInt32();
    }

    /// <summary><c>int32 count, count x string</c>.</summary>
    private static void ReadStrings(GdcReader reader)
    {
        var count = ReadCount(reader);
        for (var i = 0; i < count; i++)
            reader.ReadString();
    }

    internal static int ReadCount(GdcReader reader)
    {
        var count = reader.ReadInt32();
        if (count is < 0 or > MaxCount)
            throw new InvalidDataException($"Invalid count {count} at offset {reader.Position - 4}.");
        return count;
    }

    #endregion
}

/// <summary>Values of one bag item in <see cref="CharacterFile.Tokens"/>: <c>[Start, End)</c>, its x and y among them.</summary>
public sealed record ItemTokenLayout(int Start, int End, int XToken, int YToken);

/// <summary>
/// Values of one bag block in <see cref="CharacterFile.Tokens"/>: id at <c>Start</c>, length at <c>Start + 1</c>, a
/// byte at <c>Start + 2</c>, the item count, the items, and the end marker at <c>EndToken</c>.
/// </summary>
public sealed record BagTokenLayout(int Start, int CountToken, int EndToken, IReadOnlyList<ItemTokenLayout> Items);

/// <summary>Values of the inventory block: its length, its end marker and its bags (stored one after another).</summary>
public sealed record InventoryTokenLayout(int LengthToken, int EndToken, IReadOnlyList<BagTokenLayout> Bags);

/// <summary>
/// Values of one equipment slot in <see cref="CharacterFile.Tokens"/>: the item at <c>[Start, End)</c> (an empty item
/// in an empty slot), then the "attached" flag at <c>AttachedToken</c>.
/// </summary>
public sealed record SlotTokenLayout(EquipmentSlot Slot, int WeaponSet, int Start, int End, int AttachedToken);

/// <summary>
/// Values of one stash tab in <see cref="CharacterFile.Tokens"/>: id at <c>Start</c>, length at <c>Start + 1</c>, width
/// and height, the item count at <c>CountToken</c>, the items, the zero fields from <c>ItemsEnd</c> and the end marker at
/// <c>EndToken</c>. The x and y of every item are floats.
/// </summary>
public sealed record StashTabTokenLayout(int Start, int CountToken, int ItemsEnd, int EndToken, IReadOnlyList<ItemTokenLayout> Items);

/// <summary>Values of the stash block (4): its length, its end marker and its tabs (stored one after another).</summary>
public sealed record StashTokenLayout(int LengthToken, int EndToken, IReadOnlyList<StashTabTokenLayout> Tabs);

/// <summary>File offsets of the "modified" and "unlocked" flags and of the value of one faction (block 13).</summary>
public sealed record FactionValueOffsets(int Modified, int Unlocked, int Value);

/// <summary>Values of one list of 16-byte ids: the count at <c>CountToken</c>, the ids' bytes up to <c>End</c> (exclusive).</summary>
public sealed record UidListTokenLayout(int CountToken, int End);

/// <summary>
/// Values of the waypoint block (6): its length, its end marker and its three lists (Normal, Elite, Ultimate), stored
/// one after another.
/// </summary>
public sealed record WaypointTokenLayout(int LengthToken, int EndToken, IReadOnlyList<UidListTokenLayout> Lists);

/// <summary>
/// Values of the equipment: the inventory block's length and end marker (the equipment is stored at its end) and the
/// 16 slots in the file's order.
/// </summary>
public sealed record EquipmentTokenLayout(int LengthToken, int EndToken, IReadOnlyList<SlotTokenLayout> Slots);
