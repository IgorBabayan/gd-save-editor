using System.Buffers.Binary;
using System.IO;
using System.Text;
using K4os.Compression.LZ4;
using Microsoft.Win32.SafeHandles;

namespace GD.SaveEditor.Services.GameData.Archives;

/// <summary>One file stored in an .arc archive (e.g. <c>tags_items.txt</c> in <c>Text_EN.arc</c>).</summary>
public sealed record ArcEntry(
    string Name,
    int StorageType,
    long Offset,
    int CompressedSize,
    int DecompressedSize,
    uint Hash,
    long FileTime,
    int PartCount,
    int FirstPartIndex);

/// <summary>
/// Reads Grim Dawn resource archives (<c>Text_EN.arc</c>, <c>Items.arc</c>, ...), format version 3:
/// <code>
/// header (28 bytes): int32 magic ("ARC\0"), int32 version (3), int32 fileCount, int32 partCount,
///                    int32 partTableSize, int32 nameTableSize, int32 partTableOffset
/// part table:        partCount x (int32 offset, int32 compressedSize, int32 decompressedSize), LZ4 blocks
/// name table:        right after the part table, ASCII names
/// file table:        right after the name table, fileCount x 44 bytes (see <see cref="ArcEntry"/>)
/// </code>
/// </summary>
public sealed class ArcReader : IDisposable
{
    public const int Magic = 0x435241; // "ARC\0"
    public const int SupportedVersion = 3;

    private const int HeaderSize = 28;
    private const int PartSize = 12;
    private const int EntrySize = 44;

    private readonly SafeFileHandle _handle;
    private readonly long _length;
    private readonly int _partTableOffset;

    private ArcReader(SafeFileHandle handle, long length, int partTableOffset, IReadOnlyList<ArcEntry> entries)
    {
        _handle = handle;
        _length = length;
        _partTableOffset = partTableOffset;
        Entries = entries;
    }

    /// <summary>Files of the archive, in the archive's order (entries without a name are left out).</summary>
    public IReadOnlyList<ArcEntry> Entries { get; }

    /// <summary>Whether <paramref name="path"/> starts with a version 3 .arc header.</summary>
    public static bool LooksValid(string path)
    {
        try
        {
            using var handle = File.OpenHandle(path);
            Span<byte> header = stackalloc byte[8];
            return RandomAccess.Read(handle, header, 0) == 8
                   && BinaryPrimitives.ReadInt32LittleEndian(header) == Magic
                   && BinaryPrimitives.ReadInt32LittleEndian(header[4..]) == SupportedVersion;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <exception cref="InvalidDataException">The file is not a version 3 .arc archive or is damaged.</exception>
    public static ArcReader Open(string path)
    {
        var handle = File.OpenHandle(path);
        try
        {
            var length = RandomAccess.GetLength(handle);
            var header = ReadExactly(handle, length, 0, HeaderSize);

            if (BinaryPrimitives.ReadInt32LittleEndian(header) != Magic)
                throw new InvalidDataException($"{Path.GetFileName(path)} is not a game archive.");
            var version = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
            if (version != SupportedVersion)
                throw new InvalidDataException($"{Path.GetFileName(path)} has archive version {version}; only version {SupportedVersion} is supported.");

            var fileCount = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8));
            var partTableSize = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(16));
            var nameTableSize = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(20));
            var partTableOffset = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(24));

            var nameTableOffset = (long)partTableOffset + partTableSize;
            var names = ReadExactly(handle, length, nameTableOffset, nameTableSize);
            var table = ReadExactly(handle, length, nameTableOffset + nameTableSize, checked(fileCount * EntrySize));

            var entries = new List<ArcEntry>(fileCount);
            for (var i = 0; i < fileCount; i++)
            {
                var e = table.AsSpan(i * EntrySize, EntrySize);
                var nameLength = BinaryPrimitives.ReadInt32LittleEndian(e[36..]);
                var nameOffset = BinaryPrimitives.ReadInt32LittleEndian(e[40..]);
                if (nameLength <= 0 || nameOffset < 0 || nameOffset + nameLength > names.Length)
                    continue;

                entries.Add(new ArcEntry(
                    Name: Encoding.ASCII.GetString(names, nameOffset, nameLength).TrimEnd('\0'),
                    StorageType: BinaryPrimitives.ReadInt32LittleEndian(e),
                    Offset: BinaryPrimitives.ReadUInt32LittleEndian(e[4..]),
                    CompressedSize: BinaryPrimitives.ReadInt32LittleEndian(e[8..]),
                    DecompressedSize: BinaryPrimitives.ReadInt32LittleEndian(e[12..]),
                    Hash: BinaryPrimitives.ReadUInt32LittleEndian(e[16..]),
                    FileTime: BinaryPrimitives.ReadInt64LittleEndian(e[20..]),
                    PartCount: BinaryPrimitives.ReadInt32LittleEndian(e[28..]),
                    FirstPartIndex: BinaryPrimitives.ReadInt32LittleEndian(e[32..])));
            }

            return new ArcReader(handle, length, partTableOffset, entries);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// <summary>Contents of <paramref name="entry"/>, decompressed.</summary>
    /// <exception cref="InvalidDataException">The entry is damaged.</exception>
    public byte[] ReadFile(ArcEntry entry)
    {
        // Stored as is in one piece.
        if (entry.StorageType == 1 && entry.CompressedSize == entry.DecompressedSize)
            return ReadExactly(_handle, _length, entry.Offset, entry.CompressedSize);

        using var output = new MemoryStream(Math.Max(0, entry.DecompressedSize));
        for (var i = 0; i < entry.PartCount; i++)
        {
            var part = ReadExactly(_handle, _length, _partTableOffset + (long)(entry.FirstPartIndex + i) * PartSize, PartSize);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(part);
            var compressedSize = BinaryPrimitives.ReadInt32LittleEndian(part.AsSpan(4));
            var decompressedSize = BinaryPrimitives.ReadInt32LittleEndian(part.AsSpan(8));

            var compressed = ReadExactly(_handle, _length, offset, compressedSize);
            if (compressedSize == decompressedSize)
            {
                output.Write(compressed);
                continue;
            }

            var decompressed = new byte[decompressedSize];
            if (LZ4Codec.Decode(compressed, decompressed) != decompressedSize)
                throw new InvalidDataException($"{entry.Name} could not be decompressed.");
            output.Write(decompressed);
        }

        return output.ToArray();
    }

    public void Dispose() => _handle.Dispose();

    private static byte[] ReadExactly(SafeFileHandle handle, long fileLength, long offset, int count)
    {
        if (offset < 0 || count < 0 || offset + count > fileLength)
            throw new InvalidDataException("The archive points outside the file.");

        var buffer = new byte[count];
        var read = 0;
        while (read < count)
        {
            var n = RandomAccess.Read(handle, buffer.AsSpan(read), offset + read);
            if (n == 0)
                throw new InvalidDataException("Unexpected end of the archive.");
            read += n;
        }

        return buffer;
    }
}
