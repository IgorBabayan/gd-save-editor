using System.Buffers.Binary;
using System.IO;
using System.Text;
using K4os.Compression.LZ4;

namespace GD.SaveEditor.Services.GameData.Archives;

/// <summary>Value type of one field of an .arz record.</summary>
public enum ArzValueType : short
{
    Int = 0,
    Float = 1,
    String = 2,
    Bool = 3
}

/// <summary>One field (<c>name = value[,value...]</c>) of an .arz record.</summary>
public sealed record ArzField(string Name, ArzValueType Type, IReadOnlyList<object> Values);

/// <summary>One record (a .dbr file) of an .arz database, e.g. <c>records/items/gearhead/a01_head001.dbr</c>.</summary>
public sealed record ArzRecord(string Name, string Type, IReadOnlyList<ArzField> Fields)
{
    /// <summary>First value of <paramref name="fieldName"/> as text, or <c>null</c>.</summary>
    public string? GetString(string fieldName)
    {
        for (var i = Fields.Count - 1; i >= 0; i--)
        {
            var field = Fields[i];
            if (field.Values.Count > 0 && string.Equals(field.Name, fieldName, StringComparison.OrdinalIgnoreCase))
                return field.Values[0] as string ?? Convert.ToString(field.Values[0], System.Globalization.CultureInfo.InvariantCulture);
        }

        return null;
    }
}

/// <summary>
/// Reads Grim Dawn game databases (<c>database.arz</c>, <c>GDX1.arz</c>, ...), format version 3:
/// <code>
/// header (24 bytes): int16 unknown, int16 version (3), int32 recordTableStart, int32 recordTableSize,
///                    int32 recordCount, int32 stringTableStart, int32 stringTableSize
/// string table:      int32 count, then count x (int32 length, ASCII bytes)
/// record table:      recordCount x (int32 nameIndex, int32 typeLength, type, int32 dataOffset,
///                                   int32 compressedSize, int32 decompressedSize, int64 fileTime)
/// record data:       LZ4 block at dataOffset + 24; entries of (int16 type, int16 count, int32 nameIndex, count x 4 bytes)
/// </code>
/// </summary>
public static class ArzReader
{
    public const int HeaderSize = 24;
    public const short SupportedVersion = 3;

    /// <summary>Whether <paramref name="path"/> starts with a version 3 .arz header that fits the file.</summary>
    public static bool LooksValid(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[HeaderSize];
            if (stream.Length < HeaderSize || stream.ReadAtLeast(header, HeaderSize, throwOnEndOfStream: false) < HeaderSize)
                return false;

            var version = BinaryPrimitives.ReadInt16LittleEndian(header[2..]);
            var recordTableStart = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
            var recordTableSize = BinaryPrimitives.ReadInt32LittleEndian(header[8..]);
            var stringTableStart = BinaryPrimitives.ReadInt32LittleEndian(header[16..]);
            var stringTableSize = BinaryPrimitives.ReadInt32LittleEndian(header[20..]);

            return version == SupportedVersion
                   && recordTableStart >= HeaderSize && recordTableSize >= 0 && (long)recordTableStart + recordTableSize <= stream.Length
                   && stringTableStart >= HeaderSize && stringTableSize >= 0 && (long)stringTableStart + stringTableSize <= stream.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads the records whose name passes <paramref name="include"/> (all records when it is <c>null</c>).
    /// Single-value fields that are 0 or empty are left out, as the game treats them as unset.
    /// </summary>
    /// <param name="progress">Fraction (0..1) of the record table read so far.</param>
    /// <exception cref="InvalidDataException">The file is not a version 3 .arz database or is damaged.</exception>
    public static List<ArzRecord> ReadRecords(
        string path,
        Func<string, bool>? include = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var data = File.ReadAllBytes(path);
        var span = (ReadOnlySpan<byte>)data;
        if (span.Length < HeaderSize)
            throw new InvalidDataException($"{Path.GetFileName(path)} is too short to be a game database.");

        var version = BinaryPrimitives.ReadInt16LittleEndian(span[2..]);
        if (version != SupportedVersion)
            throw new InvalidDataException($"{Path.GetFileName(path)} has database version {version}; only version {SupportedVersion} is supported.");

        var recordTableStart = BinaryPrimitives.ReadInt32LittleEndian(span[4..]);
        var recordCount = BinaryPrimitives.ReadInt32LittleEndian(span[12..]);
        var stringTableStart = BinaryPrimitives.ReadInt32LittleEndian(span[16..]);
        var stringTableSize = BinaryPrimitives.ReadInt32LittleEndian(span[20..]);

        var strings = ReadStringTable(data, stringTableStart, stringTableSize);

        var records = new List<ArzRecord>();
        var reader = new SpanReader(data, recordTableStart);
        var reportEvery = Math.Max(1, recordCount / 200);
        for (var i = 0; i < recordCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = GetString(strings, reader.ReadInt32());
            var type = reader.ReadAsciiString();
            var dataOffset = reader.ReadInt32();
            var compressedSize = reader.ReadInt32();
            var decompressedSize = reader.ReadInt32();
            reader.Skip(8); // file time

            if (include is null || include(name))
                records.Add(new ArzRecord(name, type, ReadFields(data, strings, name, dataOffset, compressedSize, decompressedSize)));

            if (i % reportEvery == 0)
                progress?.Report((double)i / recordCount);
        }

        progress?.Report(1);
        return records;
    }

    private static List<string> ReadStringTable(byte[] data, int start, int size)
    {
        var strings = new List<string>();
        var reader = new SpanReader(data, start);
        var end = (long)start + size;

        // Normally a single block (count + strings); further blocks are read the same way if present.
        while (reader.Position + 4 <= end)
        {
            var count = reader.ReadInt32();
            for (var i = 0; i < count; i++)
                strings.Add(reader.ReadAsciiString());
        }

        return strings;
    }

    private static List<ArzField> ReadFields(byte[] data, List<string> strings, string recordName, int dataOffset, int compressedSize, int decompressedSize)
    {
        var start = (long)dataOffset + HeaderSize;
        if (dataOffset < 0 || compressedSize < 0 || decompressedSize < 0 || start + compressedSize > data.Length)
            throw new InvalidDataException($"Record {recordName} points outside the database file.");

        var decompressed = new byte[decompressedSize];
        var decoded = LZ4Codec.Decode(data.AsSpan((int)start, compressedSize), decompressed);
        if (decoded != decompressedSize)
            throw new InvalidDataException($"Record {recordName} could not be decompressed.");

        var fields = new List<ArzField>();
        var reader = new SpanReader(decompressed, 0);
        while (reader.Position + 8 <= decompressed.Length)
        {
            var type = (ArzValueType)reader.ReadInt16();
            var count = reader.ReadUInt16();
            var name = GetString(strings, reader.ReadInt32());

            var values = new object[count];
            for (var i = 0; i < count; i++)
            {
                values[i] = type switch
                {
                    ArzValueType.Float => reader.ReadSingle(),
                    ArzValueType.String => GetString(strings, reader.ReadInt32()),
                    ArzValueType.Bool => reader.ReadInt32() != 0,
                    _ => reader.ReadInt32()
                };
            }

            if (count == 1 && IsUnset(values[0]))
                continue;

            fields.Add(new ArzField(name, type, values));
        }

        return fields;
    }

    private static bool IsUnset(object value) => value switch
    {
        int i => i == 0,
        float f => f == 0f,
        bool b => !b,
        string s => s.Length == 0,
        _ => false
    };

    private static string GetString(List<string> strings, int index) =>
        index >= 0 && index < strings.Count
            ? strings[index]
            : throw new InvalidDataException($"String index {index} is outside the string table.");

    /// <summary>Little-endian reader over a byte array.</summary>
    private struct SpanReader(byte[] data, long position)
    {
        public long Position { get; private set; } = position;

        public short ReadInt16() => BinaryPrimitives.ReadInt16LittleEndian(Take(2));

        public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));

        public int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));

        public float ReadSingle() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));

        public string ReadAsciiString()
        {
            var length = ReadInt32();
            if (length < 0)
                throw new InvalidDataException("Negative string length.");
            return Encoding.ASCII.GetString(Take(length));
        }

        public void Skip(int count) => Take(count);

        private ReadOnlySpan<byte> Take(int count)
        {
            if (Position + count > data.Length)
                throw new InvalidDataException("Unexpected end of the database file.");
            var span = data.AsSpan((int)Position, count);
            Position += count;
            return span;
        }
    }
}
