using System.IO;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Services.Saves;

/// <summary>
/// Writes changes into the bytes of a character file. Every value of a <c>player.gdc</c> moves the encryption key on,
/// so one changed value changes the encrypted bytes of everything after it: the file is read value by value
/// (<see cref="CharacterFile"/>), the values are changed (the iron bits) or rebuilt (the bag blocks, when items moved)
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
    /// <paramref name="bags"/>. Each item keeps all its values; only its x and y change. The lengths of the bag blocks
    /// and of the inventory block are computed again.
    /// </summary>
    private static void RebuildBags(List<GdcToken> tokens, CharacterFile original, IReadOnlyList<IReadOnlyList<PlacedItem>> bags)
    {
        if (original.InventoryLayout is not { } layout)
            throw new InvalidDataException("The bags could not be found in the save file.");
        if (bags.Count != layout.Bags.Count)
            throw new InvalidDataException($"The save file has {layout.Bags.Count} bags, not {bags.Count}.");

        // Every item of the file exactly once: nothing is lost or copied.
        var placed = bags.SelectMany(b => b).Select(p => (p.FromBag, p.FromIndex)).ToList();
        var expected = layout.Bags.SelectMany((b, bag) => b.Items.Select((_, index) => (bag, index))).ToList();
        if (placed.Count != expected.Count || placed.Distinct().Count() != placed.Count || !placed.All(expected.Contains))
            throw new InvalidDataException("The moved items do not match the items of the save file.");

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

        // The inventory block holds the same items, but compute its length anyway rather than assume it.
        var inventoryEnd = layout.EndToken - count + segment.Count;
        tokens[layout.LengthToken] = tokens[layout.LengthToken] with { Value = SizeOf(tokens, layout.LengthToken + 1, inventoryEnd) };
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
