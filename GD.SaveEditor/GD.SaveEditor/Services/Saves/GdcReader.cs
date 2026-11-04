using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace GD.SaveEditor.Services.Saves;

/// <summary>How a value of <c>player.gdc</c> is encrypted (see <see cref="GdcReader"/>).</summary>
public enum GdcTokenKind : byte
{
    /// <summary>int32 XOR-ed with the whole key; its 4 raw bytes move the key on.</summary>
    UInt32,

    /// <summary>int32 XOR-ed with the whole key that does not move the key on (block lengths and end markers).</summary>
    UInt32KeyUnchanged,

    /// <summary>A byte XOR-ed with the low byte of the key; it moves the key on.</summary>
    Byte
}

/// <summary>One decrypted value of <c>player.gdc</c>: where it is, how it is encrypted and its plain value.</summary>
public readonly record struct GdcToken(int Offset, GdcTokenKind Kind, uint Value)
{
    public int Size => Kind == GdcTokenKind.Byte ? 1 : 4;
}

/// <summary>Where a <see cref="GdcReader"/> is, to go back to it with <see cref="GdcReader.Restore"/>.</summary>
public readonly record struct GdcReaderState(int Position, uint Key, int TokenCount);

/// <summary>
/// Decrypting reader for Grim Dawn character files (<c>player.gdc</c>).
/// The first 4 bytes XOR <c>0x55555555</c> are the seed of a 256-entry key table; every value is XOR-ed with the
/// running key, and every raw byte read moves the key on (<c>key ^= table[rawByte]</c>).
/// <para>
/// Since the key depends on the raw bytes, changing one value changes the encryption of everything after it. With
/// <c>recordTokens</c> the reader keeps every value it read (<see cref="Tokens"/>), so <see cref="GdcWriter"/> can
/// encrypt the file again with changed values.
/// </para>
/// </summary>
public sealed class GdcReader
{
    private const uint SeedMask = 0x55555555;
    private const uint TableMultiplier = 39916801;

    private readonly byte[] _data;
    private readonly uint[] _table = new uint[256];
    private uint _key;
    private int _position;
    private readonly List<GdcToken>? _tokens;

    public GdcReader(byte[] data, bool recordTokens = false)
    {
        _data = data;
        _tokens = recordTokens ? [] : null;
        var seed = BinaryPrimitives.ReadUInt32LittleEndian(Take(4)) ^ SeedMask;
        _key = seed;

        var value = seed;
        for (var i = 0; i < _table.Length; i++)
        {
            value = unchecked(((value >> 1) | (value << 31)) * TableMultiplier);
            _table[i] = value;
        }
    }

    public int Position => _position;

    /// <summary>Length of the whole file.</summary>
    public int Length => _data.Length;

    /// <summary>Every value read so far, in file order; empty unless the reader was created with <c>recordTokens</c>.</summary>
    public IReadOnlyList<GdcToken> Tokens => _tokens ?? (IReadOnlyList<GdcToken>)[];

    /// <summary>Position and key, to go back after a part that could not be read (see <see cref="Restore"/>).</summary>
    public GdcReaderState Snapshot() => new(_position, _key, _tokens?.Count ?? 0);

    /// <summary>Goes back to <paramref name="state"/>, forgetting the values read since.</summary>
    public void Restore(GdcReaderState state)
    {
        (_position, _key) = (state.Position, state.Key);
        if (_tokens is not null && _tokens.Count > state.TokenCount)
            _tokens.RemoveRange(state.TokenCount, _tokens.Count - state.TokenCount);
    }

    public int ReadInt32() => unchecked((int)ReadUInt32());

    public uint ReadUInt32()
    {
        var offset = _position;
        var raw = Take(4);
        var value = BinaryPrimitives.ReadUInt32LittleEndian(raw) ^ _key;
        _tokens?.Add(new GdcToken(offset, GdcTokenKind.UInt32, value));
        foreach (var b in raw)
            _key ^= _table[b];
        return value;
    }

    /// <summary>
    /// An int32 that is decrypted with the current key but does not move the key on: the length and the end marker
    /// of a block, and the zero after the header.
    /// </summary>
    public uint ReadUInt32KeyUnchanged()
    {
        var offset = _position;
        var value = BinaryPrimitives.ReadUInt32LittleEndian(Take(4)) ^ _key;
        _tokens?.Add(new GdcToken(offset, GdcTokenKind.UInt32KeyUnchanged, value));
        return value;
    }

    public float ReadSingle() => BitConverter.UInt32BitsToSingle(ReadUInt32());

    /// <summary>
    /// Reads past <paramref name="count"/> bytes; the key moves on exactly as if they were read as values. They are
    /// recorded as single bytes, which is only right for parts that are byte values (the character id): a file with
    /// other skipped parts cannot be encrypted again.
    /// </summary>
    public void Skip(int count)
    {
        if (count < 0 || count > _data.Length - _position)
            throw new InvalidDataException($"Invalid length {count} at offset {_position}.");
        for (var i = 0; i < count; i++)
            ReadByte();
    }

    /// <summary>
    /// Starts a block: <c>int32 id</c>, then its length (key unchanged). Returns the id and the offset the block ends at.
    /// </summary>
    public (uint Id, int End) ReadBlockStart()
    {
        var id = ReadUInt32();
        var length = ReadUInt32KeyUnchanged();
        if (length > (uint)(_data.Length - _position))
            throw new InvalidDataException($"Block {id} at offset {_position} is longer than the file.");
        return (id, _position + (int)length);
    }

    /// <summary>Ends a block: the position must be its end, followed by a zero int32 (key unchanged).</summary>
    public void ReadBlockEnd(int end)
    {
        if (_position != end)
            throw new InvalidDataException($"Block ends at offset {_position}, expected {end}.");
        if (ReadUInt32KeyUnchanged() != 0)
            throw new InvalidDataException($"Missing block end marker at offset {end}.");
    }

    public byte ReadByte()
    {
        var offset = _position;
        var raw = Take(1)[0];
        var value = (byte)(raw ^ _key);
        _tokens?.Add(new GdcToken(offset, GdcTokenKind.Byte, value));
        _key ^= _table[raw];
        return value;
    }

    public bool ReadBool() => ReadByte() != 0;

    /// <summary>Length-prefixed single-byte string (class names, record paths).</summary>
    public string ReadString()
    {
        var length = ReadLength(1);
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
            bytes[i] = ReadByte();
        return Encoding.Latin1.GetString(bytes);
    }

    /// <summary>Length-prefixed UTF-16 string (the character name).</summary>
    public string ReadWideString()
    {
        var length = ReadLength(2);
        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = (char)(ReadByte() | (ReadByte() << 8));
        return new string(chars);
    }

    private int ReadLength(int bytesPerChar)
    {
        var length = ReadInt32();
        if (length < 0 || (long)length * bytesPerChar > _data.Length - _position)
            throw new InvalidDataException($"Invalid string length {length} at offset {_position}.");
        return length;
    }

    private ReadOnlySpan<byte> Take(int count)
    {
        if (_position + count > _data.Length)
            throw new InvalidDataException("Unexpected end of the character file.");
        var span = _data.AsSpan(_position, count);
        _position += count;
        return span;
    }
}
