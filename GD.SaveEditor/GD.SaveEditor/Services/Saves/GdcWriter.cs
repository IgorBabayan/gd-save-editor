using System.Buffers.Binary;
using System.IO;

namespace GD.SaveEditor.Services.Saves;

/// <summary>
/// Encrypts a character file again from the values a <see cref="GdcReader"/> recorded, some of them changed, added or
/// removed. The seed (first 4 bytes) is kept, so a file without changes comes out byte for byte as it was read.
/// </summary>
public static class GdcWriter
{
    private const uint SeedMask = 0x55555555;
    private const uint TableMultiplier = 39916801;

    /// <summary>
    /// The file <paramref name="original"/> with the values at the offsets of <paramref name="changes"/> replaced.
    /// <paramref name="tokens"/> must be every value of the file in order, as read with <c>recordTokens</c>.
    /// </summary>
    /// <exception cref="InvalidDataException">The tokens do not cover the whole file, or a change is not at a value.</exception>
    public static byte[] Encode(byte[] original, IReadOnlyList<GdcToken> tokens, IReadOnlyDictionary<int, uint> changes)
    {
        EnsureCoversFile(original, tokens);

        var changed = new List<GdcToken>(tokens.Count);
        var applied = 0;
        foreach (var token in tokens)
        {
            if (changes.TryGetValue(token.Offset, out var value))
            {
                changed.Add(token with { Value = value });
                applied++;
            }
            else
            {
                changed.Add(token);
            }
        }

        if (applied != changes.Count)
            throw new InvalidDataException("A changed value is not at the start of a value of the character file.");

        return EncodeSequence(original, changed);
    }

    /// <summary>
    /// A file with the seed of <paramref name="original"/> and the values of <paramref name="tokens"/>, written one after
    /// another in their order; their offsets are ignored, so values can be added, removed or moved.
    /// </summary>
    public static byte[] EncodeSequence(byte[] original, IReadOnlyList<GdcToken> tokens)
    {
        if (original.Length < 4)
            throw new InvalidDataException("The character file is too short.");

        var output = new byte[4 + tokens.Sum(t => t.Size)];
        original.AsSpan(0, 4).CopyTo(output);

        var seed = BinaryPrimitives.ReadUInt32LittleEndian(original) ^ SeedMask;
        var table = new uint[256];
        var value = seed;
        for (var i = 0; i < table.Length; i++)
        {
            value = unchecked(((value >> 1) | (value << 31)) * TableMultiplier);
            table[i] = value;
        }

        var key = seed;
        var position = 4;
        Span<byte> raw = stackalloc byte[4];
        foreach (var token in tokens)
        {
            switch (token.Kind)
            {
                case GdcTokenKind.UInt32:
                    BinaryPrimitives.WriteUInt32LittleEndian(raw, token.Value ^ key);
                    raw.CopyTo(output.AsSpan(position));
                    foreach (var b in raw)
                        key ^= table[b];
                    break;

                case GdcTokenKind.UInt32KeyUnchanged:
                    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(position), token.Value ^ key);
                    break;

                case GdcTokenKind.Byte:
                    if (token.Value > byte.MaxValue)
                        throw new InvalidDataException($"Value {token.Value} does not fit a byte.");
                    var rawByte = (byte)(token.Value ^ key);
                    output[position] = rawByte;
                    key ^= table[rawByte];
                    break;
            }

            position += token.Size;
        }

        return output;
    }

    /// <summary>The tokens must follow each other from offset 4 to the end of the file, so nothing is left out.</summary>
    public static void EnsureCoversFile(byte[] original, IReadOnlyList<GdcToken> tokens)
    {
        var position = 4;
        foreach (var token in tokens)
        {
            if (token.Offset != position)
                throw new InvalidDataException($"The character file was not read completely (offset {position}).");
            position += token.Size;
        }

        if (position != original.Length)
            throw new InvalidDataException($"The character file was not read completely (offset {position}).");
    }
}
