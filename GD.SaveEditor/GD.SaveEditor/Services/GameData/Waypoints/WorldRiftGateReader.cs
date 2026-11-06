using System.Buffers.Binary;
using System.IO;
using System.Text;
using GD.SaveEditor.Services.GameData.Archives;

namespace GD.SaveEditor.Services.GameData.Waypoints;

/// <summary>A rift gate of the world map: its id (as the save file stores it) and the record of the gate.</summary>
/// <param name="Uid">The 16 bytes of the id as lowercase hex, in file order (see <see cref="GameWaypoint.Uid"/>).</param>
public sealed record WorldRiftGate(string Uid, string RecordPath);

/// <summary>
/// Reads the rift gates (waypoints) of the game world from <c>world001.map</c> in a <c>Levels.arc</c>.
/// <para>
/// The map's instance section holds named tables of entity ids; the rift gates are the one named
/// <c>"- World Riftgates"</c>:
/// <code>
/// int32 0, int32 3, string name ("- World Riftgates"), string name2,
/// int32 count, count x (16-byte uid, string record, 28 bytes)
/// </code>
/// (strings are <c>int32 length, ASCII</c>). The layout of the rest of the map is not needed: the table is found by
/// its header, the same way the gd-edit save editor does it.
/// </para>
/// </summary>
public static class WorldRiftGateReader
{
    private const string MapFileName = "world001.map";
    private const string TableName = "- World Riftgates";
    private const int EntryTailSize = 28;
    private const int MaxGates = 10_000;

    /// <summary>The rift gates of <c>world001.map</c> in <paramref name="levelsArchivePath"/>, in the map's order.</summary>
    /// <exception cref="InvalidDataException">The archive has no world map, or its rift gate table cannot be read.</exception>
    public static IReadOnlyList<WorldRiftGate> Read(string levelsArchivePath)
    {
        using var archive = ArcReader.Open(levelsArchivePath);
        var entry = archive.Entries.FirstOrDefault(e => string.Equals(e.Name, MapFileName, StringComparison.OrdinalIgnoreCase))
                    ?? archive.Entries.FirstOrDefault(e => e.Name.EndsWith("/" + MapFileName, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException($"{Path.GetFileName(levelsArchivePath)} has no {MapFileName}.");

        return ReadMap(archive.ReadFile(entry));
    }

    /// <summary>The rift gates of the contents of a <c>world001.map</c>.</summary>
    /// <exception cref="InvalidDataException">The rift gate table cannot be found or read.</exception>
    public static IReadOnlyList<WorldRiftGate> ReadMap(ReadOnlySpan<byte> map)
    {
        var header = BuildHeader();
        var searchFrom = 0;
        string? lastError = null;
        while (searchFrom < map.Length)
        {
            var found = map[searchFrom..].IndexOf(header);
            if (found < 0)
                break;

            var offset = searchFrom + found;
            try
            {
                return ReadTable(map, offset + header.Length);
            }
            catch (InvalidDataException ex)
            {
                // The name can also appear elsewhere: try the next one.
                lastError = ex.Message;
                searchFrom = offset + 1;
            }
        }

        throw new InvalidDataException(lastError is null
            ? $"The world map has no \"{TableName}\" table."
            : $"The \"{TableName}\" table of the world map could not be read: {lastError}");
    }

    /// <summary><c>int32 0, int32 3, int32 length, "- World Riftgates"</c>.</summary>
    private static byte[] BuildHeader()
    {
        var name = Encoding.ASCII.GetBytes(TableName);
        var header = new byte[12 + name.Length];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0), 0);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), 3);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), name.Length);
        name.CopyTo(header, 12);
        return header;
    }

    /// <summary>Reads from the second name of the table header to its last entry.</summary>
    private static List<WorldRiftGate> ReadTable(ReadOnlySpan<byte> map, int position)
    {
        ReadString(map, ref position); // second name of the table

        var count = ReadInt32(map, ref position);
        if (count is <= 0 or > MaxGates)
            throw new InvalidDataException($"invalid number of rift gates ({count})");

        var gates = new List<WorldRiftGate>(count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            var uid = Convert.ToHexStringLower(Take(map, ref position, 16));
            var record = ReadString(map, ref position);
            Take(map, ref position, EntryTailSize);

            if (!record.StartsWith("records/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"rift gate {i + 1} has no record (\"{record}\")");
            if (seen.Add(uid))
                gates.Add(new WorldRiftGate(uid, record.Replace('\\', '/')));
        }

        return gates;
    }

    private static string ReadString(ReadOnlySpan<byte> map, ref int position)
    {
        var length = ReadInt32(map, ref position);
        if (length is < 0 or > 1024)
            throw new InvalidDataException($"invalid string length {length} at offset {position - 4}");

        var bytes = Take(map, ref position, length);
        foreach (var b in bytes)
        {
            if (b is < 0x20 or > 0x7E)
                throw new InvalidDataException($"unexpected text at offset {position - length}");
        }

        return Encoding.ASCII.GetString(bytes);
    }

    private static int ReadInt32(ReadOnlySpan<byte> map, ref int position) =>
        BinaryPrimitives.ReadInt32LittleEndian(Take(map, ref position, 4));

    private static ReadOnlySpan<byte> Take(ReadOnlySpan<byte> map, ref int position, int count)
    {
        if (count < 0 || position > map.Length - count)
            throw new InvalidDataException("the table runs past the end of the map");

        var span = map.Slice(position, count);
        position += count;
        return span;
    }
}
