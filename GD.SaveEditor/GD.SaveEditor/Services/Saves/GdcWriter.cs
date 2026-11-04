using System.Buffers.Binary;
using System.IO;

namespace GD.SaveEditor.Services.Saves;

/// <summary>
/// Encrypts a character file again from the values a <see cref="GdcReader"/> recorded, with some of them changed.
/// The seed (first 4 bytes) is kept, so a file without changes comes out byte for byte as it was read.
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
        if (original.Length < 4)
            throw new InvalidDataException("The character file is too short.");

        var output = (byte[])original.Clone();
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
        var applied = 0;
        Span<byte> raw = stackalloc byte[4];
        foreach (var token in tokens)
        {
            if (token.Offset != position)
                throw new InvalidDataException($"The character file was not read completely (offset {position}).");

            var plain = token.Value;
            if (changes.TryGetValue(token.Offset, out var changed))
            {
                plain = changed;
                applied++;
            }

            switch (token.Kind)
            {
                case GdcTokenKind.UInt32:
                    BinaryPrimitives.WriteUInt32LittleEndian(raw, plain ^ key);
                    raw.CopyTo(output.AsSpan(position));
                    foreach (var b in raw)
                        key ^= table[b];
                    break;

                case GdcTokenKind.UInt32KeyUnchanged:
                    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(position), plain ^ key);
                    break;

                case GdcTokenKind.Byte:
                    if (plain > byte.MaxValue)
                        throw new InvalidDataException($"Value {plain} does not fit the byte at offset {position}.");
                    var rawByte = (byte)(plain ^ key);
                    output[position] = rawByte;
                    key ^= table[rawByte];
                    break;
            }

            position += token.Size;
        }

        if (position != original.Length)
            throw new InvalidDataException($"The character file was not read completely (offset {position}).");
        if (applied != changes.Count)
            throw new InvalidDataException("A changed value is not at the start of a value of the character file.");

        return output;
    }
}
