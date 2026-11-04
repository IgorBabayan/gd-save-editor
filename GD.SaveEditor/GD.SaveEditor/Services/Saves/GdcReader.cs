using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace GD.SaveEditor.Services.Saves;

/// <summary>
/// Decrypting reader for Grim Dawn character files (<c>player.gdc</c>).
/// The first 4 bytes XOR <c>0x55555555</c> are the seed of a 256-entry key table; every value is XOR-ed with the
/// running key, and every raw byte read moves the key on (<c>key ^= table[rawByte]</c>).
/// </summary>
public sealed class GdcReader
{
    private const uint SeedMask = 0x55555555;
    private const uint TableMultiplier = 39916801;

    private readonly byte[] _data;
    private readonly uint[] _table = new uint[256];
    private uint _key;
    private int _position;

    public GdcReader(byte[] data)
    {
        _data = data;
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

    public int ReadInt32() => unchecked((int)ReadUInt32());

    public uint ReadUInt32()
    {
        var raw = Take(4);
        var value = BinaryPrimitives.ReadUInt32LittleEndian(raw) ^ _key;
        foreach (var b in raw)
            _key ^= _table[b];
        return value;
    }

    public byte ReadByte()
    {
        var raw = Take(1)[0];
        var value = (byte)(raw ^ _key);
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
