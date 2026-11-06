using System.IO;
using System.Text;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Services.Saves;

/// <summary>
/// Values of one item container (a bag of the inventory or a stash tab) in the recorded tokens: id at <c>Start</c>,
/// length at <c>Start + 1</c>, the values up to the item count at <c>CountToken</c> (a byte for a bag, the width and the
/// height for a stash tab), the items, the values from <c>ItemsEnd</c> to the end marker at <c>EndToken</c> (none for a
/// bag, zero fields for a stash tab).
/// </summary>
public sealed record ContainerTokenLayout(int Start, int CountToken, int ItemsEnd, int EndToken, IReadOnlyList<ItemTokenLayout> Items)
{
    public static ContainerTokenLayout From(BagTokenLayout bag) => new(bag.Start, bag.CountToken, bag.EndToken, bag.EndToken, bag.Items);

    public static ContainerTokenLayout From(StashTabTokenLayout tab) => new(tab.Start, tab.CountToken, tab.ItemsEnd, tab.EndToken, tab.Items);
}

/// <summary>
/// Writes items as values of a Grim Dawn file (<c>player.gdc</c>, <c>transfer.gst</c>) and rebuilds the blocks of item
/// containers with other contents. The values get offset 0: <see cref="GdcWriter.EncodeSequence"/> writes them one after
/// another.
/// </summary>
public static class ItemContainerTokens
{
    /// <summary>
    /// Replaces the container blocks of <paramref name="layouts"/> (stored one after another) in <paramref name="tokens"/>
    /// with blocks holding the items as placed in <paramref name="containers"/>. An unchanged item keeps all its values
    /// (only its cell changes); a changed or new item is written value by value (<see cref="AddItem"/>); an item of the
    /// file that is not placed is left out (removed). The lengths of the blocks are computed again. Returns how many
    /// values the tokens gained (negative: lost), for the length of the block around the containers.
    /// </summary>
    /// <param name="floatCells">The cells are stored as floats (stash tabs), not as int32 (bags).</param>
    /// <param name="what">"bags" or "stash tabs", for the messages.</param>
    public static int Rebuild(
        List<GdcToken> tokens,
        IReadOnlyList<ContainerTokenLayout> layouts,
        IReadOnlyList<IReadOnlyList<PlacedItem>> containers,
        int extraFields,
        bool floatCells,
        string what)
    {
        if (layouts.Count == 0)
            throw new InvalidDataException($"The save file has no {what}.");
        if (containers.Count != layouts.Count)
            throw new InvalidDataException($"The save file has {layouts.Count} {what}, not {containers.Count}.");

        // Every item of the file at most once (a removed item is simply not placed): nothing is copied.
        var placed = containers.SelectMany(c => c).Where(p => !p.IsNew).Select(p => (p.FromBag, p.FromIndex)).ToList();
        var expected = layouts.SelectMany((c, container) => c.Items.Select((_, index) => (container, index))).ToHashSet();
        if (placed.Distinct().Count() != placed.Count || !placed.All(expected.Contains))
            throw new InvalidDataException($"The placed items do not match the items of the {what} in the save file.");
        if (containers.SelectMany(c => c).Any(p => p.IsNew && p.Item is null))
            throw new InvalidDataException("A new item has no contents.");

        var segment = new List<GdcToken>();
        for (var container = 0; container < containers.Count; container++)
        {
            var layout = layouts[container];
            var lengthIndex = segment.Count + 1;
            for (var i = layout.Start; i < layout.CountToken; i++)      // id, length (set below), values before the count
                segment.Add(tokens[i]);
            segment.Add(tokens[layout.CountToken] with { Value = (uint)containers[container].Count });

            foreach (var item in containers[container])
            {
                if (item.X < 0 || item.Y < 0)
                    throw new InvalidDataException("An item was placed outside its container.");

                if (item.Item is { } contents)
                {
                    AddItem(segment, contents.WithExtraFieldCount(extraFields));
                    AddUInt32(segment, EncodeCell(item.X, floatCells));
                    AddUInt32(segment, EncodeCell(item.Y, floatCells));
                    continue;
                }

                var itemLayout = layouts[item.FromBag].Items[item.FromIndex];
                for (var i = itemLayout.Start; i < itemLayout.End; i++)
                {
                    var token = tokens[i];
                    // A cell that did not change keeps its value exactly as read.
                    if (i == itemLayout.XToken && DecodeCell(token.Value, floatCells) != item.X)
                        token = token with { Value = EncodeCell(item.X, floatCells) };
                    else if (i == itemLayout.YToken && DecodeCell(token.Value, floatCells) != item.Y)
                        token = token with { Value = EncodeCell(item.Y, floatCells) };
                    segment.Add(token);
                }
            }

            for (var i = layout.ItemsEnd; i < layout.EndToken; i++)         // values after the items
                segment.Add(tokens[i]);

            var endIndex = segment.Count;
            segment.Add(tokens[layout.EndToken]);                            // block end marker
            segment[lengthIndex] = segment[lengthIndex] with { Value = SizeOf(segment, lengthIndex + 1, endIndex) };
        }

        var first = layouts[0].Start;
        var count = layouts[^1].EndToken + 1 - first;
        tokens.RemoveRange(first, count);
        tokens.InsertRange(first, segment);
        return segment.Count - count;
    }

    /// <summary>The cell (<paramref name="cell"/>) as stored: an int32, or the bits of a float.</summary>
    private static uint EncodeCell(int cell, bool floatCells) =>
        floatCells ? BitConverter.SingleToUInt32Bits(cell) : (uint)cell;

    /// <summary>The cell a stored value stands for (a float cell is rounded, as the editor shows it).</summary>
    public static int DecodeCell(uint value, bool floatCells) =>
        floatCells ? (int)MathF.Round(BitConverter.UInt32BitsToSingle(value)) : (int)value;

    /// <summary>
    /// The values of <paramref name="item"/> in the order the game stores an item (see <see cref="CharacterFile"/>):
    /// <code>
    /// string base, prefix, suffix, modifier, transmute; int32 seed;
    /// string component, completionBonus; int32 componentSeed;
    /// string augment; int32 unknown, augmentSeed, var1, stackCount, extra fields
    /// </code>
    /// </summary>
    public static void AddItem(List<GdcToken> segment, SaveItem item)
    {
        AddString(segment, item.BaseRecord);
        AddString(segment, item.PrefixRecord);
        AddString(segment, item.SuffixRecord);
        AddString(segment, item.ModifierRecord);
        AddString(segment, item.TransmuteRecord);
        AddUInt32(segment, item.Seed);
        AddString(segment, item.ComponentRecord);
        AddString(segment, item.CompletionBonusRecord);
        AddUInt32(segment, item.ComponentSeed);
        AddString(segment, item.AugmentRecord);
        AddUInt32(segment, item.Unknown);
        AddUInt32(segment, item.AugmentSeed);
        AddUInt32(segment, item.Var1);
        AddUInt32(segment, item.StackCount);
        foreach (var extra in item.ExtraFields)
            AddUInt32(segment, extra);
    }

    public static void AddUInt32(List<GdcToken> segment, uint value) =>
        segment.Add(new GdcToken(0, GdcTokenKind.UInt32, value));

    /// <summary>A length-prefixed single-byte string, like <see cref="GdcReader.ReadString"/> reads it.</summary>
    public static void AddString(List<GdcToken> segment, string? value)
    {
        var bytes = Encoding.Latin1.GetBytes(value ?? string.Empty);
        AddUInt32(segment, (uint)bytes.Length);
        foreach (var b in bytes)
            segment.Add(new GdcToken(0, GdcTokenKind.Byte, b));
    }

    /// <summary>Bytes of <c>tokens[from..to)</c>.</summary>
    public static uint SizeOf(List<GdcToken> tokens, int from, int to)
    {
        var size = 0;
        for (var i = from; i < to; i++)
            size += tokens[i].Size;
        return (uint)size;
    }

    /// <summary>The written file must read back with exactly the values that were meant to be written.</summary>
    public static void Verify(IReadOnlyList<GdcToken> intended, IReadOnlyList<GdcToken> written, string file)
    {
        if (written.Count != intended.Count)
            throw new InvalidDataException($"The changed {file} did not read back correctly; nothing was written.");

        for (var i = 0; i < intended.Count; i++)
        {
            if (written[i].Kind != intended[i].Kind || written[i].Value != intended[i].Value)
                throw new InvalidDataException($"The changed {file} differs at offset {written[i].Offset}; nothing was written.");
        }
    }
}
