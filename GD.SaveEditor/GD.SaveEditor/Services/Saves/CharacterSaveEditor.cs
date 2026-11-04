using System.IO;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Services.Saves;

/// <summary>
/// Writes changes into the bytes of a character file. Every value of a <c>player.gdc</c> moves the encryption key on,
/// so one changed value changes the encrypted bytes of everything after it: the file is read value by value
/// (<see cref="CharacterFile"/>), the values are changed (the iron bits) or rebuilt (the bag blocks, when items were moved, added, changed or removed)
/// and the whole file is encrypted again (<see cref="GdcWriter"/>). The result is read back before it is returned: it
/// must hold exactly the values that were meant to be written.
/// </summary>
public sealed class CharacterSaveEditor : ICharacterSaveEditor
{
    public byte[] Apply(CharacterSave character, CharacterChanges changes)
    {
        if (changes.IsEmpty)
            return character.RawData;

        var original = CharacterFile.Read(character.RawData, recordTokens: true);
        if (original.RewriteError is { } rewriteError)
            throw new InvalidDataException($"{character.CharacterName} cannot be changed by this editor: {rewriteError}");

        GdcWriter.EnsureCoversFile(character.RawData, original.Tokens);
        var tokens = original.Tokens.ToList();

        if (changes.Money is { } money)
        {
            var index = original.MoneyOffset is { } offset ? tokens.FindIndex(t => t.Offset == offset) : -1;
            if (index < 0)
                throw new InvalidDataException($"The iron bits of {character.CharacterName} could not be found in the save file.");
            tokens[index] = tokens[index] with { Value = money };
        }

        if (changes.Bags is { } bags)
            RebuildBags(tokens, original, bags);

        var data = GdcWriter.EncodeSequence(character.RawData, tokens);
        Verify(tokens, CharacterFile.Read(data, recordTokens: true));
        return data;
    }

    /// <summary>
    /// Replaces the bag blocks in <paramref name="tokens"/> with blocks holding the items as placed in
    /// <paramref name="bags"/>. An unchanged item keeps all its values (only its x and y change); a changed or new item
    /// is written value by value (<see cref="AddItem"/>); an item of the file that is not placed is left out (removed).
    /// The lengths of the bag blocks and of the inventory block are computed again.
    /// </summary>
    private static void RebuildBags(List<GdcToken> tokens, CharacterFile original, IReadOnlyList<IReadOnlyList<PlacedItem>> bags)
    {
        if (original.InventoryLayout is not { } layout)
            throw new InvalidDataException("The bags could not be found in the save file.");
        if (bags.Count != layout.Bags.Count)
            throw new InvalidDataException($"The save file has {layout.Bags.Count} bags, not {bags.Count}.");

        // Every item of the file at most once (a removed item is simply not placed): nothing is copied.
        var placed = bags.SelectMany(b => b).Where(p => !p.IsNew).Select(p => (p.FromBag, p.FromIndex)).ToList();
        var expected = layout.Bags.SelectMany((b, bag) => b.Items.Select((_, index) => (bag, index))).ToHashSet();
        if (placed.Distinct().Count() != placed.Count || !placed.All(expected.Contains))
            throw new InvalidDataException("The placed items do not match the items of the save file.");
        if (bags.SelectMany(b => b).Any(p => p.IsNew && p.Item is null))
            throw new InvalidDataException("A new item has no contents.");

        var segment = new List<GdcToken>();
        for (var bag = 0; bag < bags.Count; bag++)
        {
            var bagLayout = layout.Bags[bag];
            segment.Add(tokens[bagLayout.Start]);                       // block id
            var lengthIndex = segment.Count;
            segment.Add(tokens[bagLayout.Start + 1]);                   // block length, set below
            segment.Add(tokens[bagLayout.Start + 2]);                   // byte before the items
            segment.Add(tokens[bagLayout.CountToken] with { Value = (uint)bags[bag].Count });

            foreach (var item in bags[bag])
            {
                if (item.X < 0 || item.Y < 0)
                    throw new InvalidDataException("An item was placed outside its bag.");

                if (item.Item is { } contents)
                {
                    AddItem(segment, contents.WithExtraFieldCount(original.ItemExtraFields));
                    segment.Add(new GdcToken(0, GdcTokenKind.UInt32, (uint)item.X));
                    segment.Add(new GdcToken(0, GdcTokenKind.UInt32, (uint)item.Y));
                    continue;
                }

                var itemLayout = layout.Bags[item.FromBag].Items[item.FromIndex];
                for (var i = itemLayout.Start; i < itemLayout.End; i++)
                {
                    var token = tokens[i];
                    if (i == itemLayout.XToken)
                        token = token with { Value = (uint)item.X };
                    else if (i == itemLayout.YToken)
                        token = token with { Value = (uint)item.Y };
                    segment.Add(token);
                }
            }

            var endIndex = segment.Count;
            segment.Add(tokens[bagLayout.EndToken]);                    // block end marker
            segment[lengthIndex] = segment[lengthIndex] with { Value = SizeOf(segment, lengthIndex + 1, endIndex) };
        }

        var first = layout.Bags[0].Start;
        var count = layout.Bags[^1].EndToken + 1 - first;
        tokens.RemoveRange(first, count);
        tokens.InsertRange(first, segment);

        // The inventory block grows or shrinks with its bags (items added or removed, or changed in length).
        var inventoryEnd = layout.EndToken - count + segment.Count;
        tokens[layout.LengthToken] = tokens[layout.LengthToken] with { Value = SizeOf(tokens, layout.LengthToken + 1, inventoryEnd) };
    }

    /// <summary>
    /// The values of <paramref name="item"/> in the order the game stores an item (see <see cref="CharacterFile"/>):
    /// <code>
    /// string base, prefix, suffix, modifier, transmute; int32 seed;
    /// string component, completionBonus; int32 componentSeed;
    /// string augment; int32 unknown, augmentSeed, var1, stackCount, extra fields
    /// </code>
    /// The offsets are 0: <see cref="GdcWriter.EncodeSequence"/> writes the values one after another.
    /// </summary>
    private static void AddItem(List<GdcToken> segment, SaveItem item)
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

    private static void AddUInt32(List<GdcToken> segment, uint value) =>
        segment.Add(new GdcToken(0, GdcTokenKind.UInt32, value));

    /// <summary>A length-prefixed single-byte string, like <see cref="GdcReader.ReadString"/> reads it.</summary>
    private static void AddString(List<GdcToken> segment, string? value)
    {
        var bytes = System.Text.Encoding.Latin1.GetBytes(value ?? string.Empty);
        AddUInt32(segment, (uint)bytes.Length);
        foreach (var b in bytes)
            segment.Add(new GdcToken(0, GdcTokenKind.Byte, b));
    }

    /// <summary>Bytes of <c>tokens[from..to)</c>.</summary>
    private static uint SizeOf(List<GdcToken> tokens, int from, int to)
    {
        var size = 0;
        for (var i = from; i < to; i++)
            size += tokens[i].Size;
        return (uint)size;
    }

    /// <summary>The written file must read back with exactly the values that were meant to be written.</summary>
    private static void Verify(IReadOnlyList<GdcToken> intended, CharacterFile written)
    {
        if (written.RewriteError is not null || written.DetailsError is not null || written.Tokens.Count != intended.Count)
            throw new InvalidDataException("The changed save file did not read back correctly; nothing was written.");

        for (var i = 0; i < intended.Count; i++)
        {
            if (written.Tokens[i].Kind != intended[i].Kind || written.Tokens[i].Value != intended[i].Value)
                throw new InvalidDataException($"The changed save file differs at offset {written.Tokens[i].Offset}; nothing was written.");
        }
    }
}
