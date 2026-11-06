using System.IO;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Services.Saves;

/// <summary>
/// The Smuggler's material storage (game version 1.3+): the shared "Components" and "Crafting Materials" pages, kept in
/// <c>reagents.gst</c> (softcore) or <c>reagents.gsh</c> (hardcore) next to the transfer stash. It holds how many of
/// every material are stored, not items with a place. It is encrypted like <c>player.gdc</c> (see <see cref="GdcReader"/>):
/// <code>
/// int32 seed, then encrypted: int32 1,
/// block 20: int32 version (1), int32 (key unchanged), string mod, int32 count,
///   count x block 0: string record, int32 amount
/// </code>
/// The game lists the records sorted by path and keeps a material it has none of left with 0.
/// </summary>
public sealed class MaterialStorageFile
{
    private const uint Magic = 1;
    private const uint BlockId = 20;
    private const int SupportedVersion = 1;
    private const int MaxEntries = 10_000;

    private MaterialStorageFile()
    {
    }

    public int Version { get; private set; }

    public string Mod { get; private set; } = string.Empty;

    /// <summary>The stored materials in the file's order.</summary>
    public IReadOnlyList<StoredMaterial> Materials { get; private set; } = [];

    /// <summary>Every value of the file in order; empty unless read with <c>recordTokens</c>.</summary>
    public IReadOnlyList<GdcToken> Tokens { get; private set; } = [];

    /// <summary>Token index of the block length, of the material count and of the block end marker.</summary>
    private (int Length, int Count, int End) _layout = (-1, -1, -1);

    /// <exception cref="InvalidDataException">Not a material storage, or one with a layout this editor does not know.</exception>
    public static MaterialStorageFile Read(byte[] data, bool recordTokens = false)
    {
        if (data.Length < 12)
            throw new InvalidDataException("The file is too short to be a Grim Dawn material storage.");

        var reader = new GdcReader(data, recordTokens);
        if (reader.ReadUInt32() != Magic)
            throw new InvalidDataException("Not a Grim Dawn material storage.");

        var blockToken = reader.TokenCount;
        var (id, end) = reader.ReadBlockStart();
        if (id != BlockId)
            throw new InvalidDataException($"Not a Grim Dawn material storage (block {id}).");

        var file = new MaterialStorageFile { Version = reader.ReadInt32() };
        if (file.Version != SupportedVersion)
            throw new InvalidDataException($"Material storage version {file.Version} is not supported.");

        reader.ReadUInt32KeyUnchanged();
        file.Mod = reader.ReadString();

        var countToken = reader.TokenCount;
        var count = reader.ReadInt32();
        if (count is < 0 or > MaxEntries)
            throw new InvalidDataException($"Invalid number of stored materials ({count}).");

        var materials = new List<StoredMaterial>(count);
        for (var i = 0; i < count; i++)
        {
            var (entryId, entryEnd) = reader.ReadBlockStart();
            if (entryId != 0)
                throw new InvalidDataException($"Unexpected block {entryId} in the material storage.");

            var record = reader.ReadString();
            var amount = reader.ReadInt32();
            if (!record.StartsWith("records/", StringComparison.OrdinalIgnoreCase) || amount < 0)
                throw new InvalidDataException("A stored material does not fit the material layout.");

            reader.ReadBlockEnd(entryEnd);
            materials.Add(new StoredMaterial(record, amount));
        }

        var endToken = reader.TokenCount;
        reader.ReadBlockEnd(end);
        if (reader.Position != data.Length)
            throw new InvalidDataException("The material storage has data after its materials this editor does not know.");

        file.Materials = materials;
        file.Tokens = reader.Tokens;
        if (recordTokens)
            file._layout = (blockToken + 1, countToken, endToken);
        return file;
    }

    /// <summary>
    /// The file's values with the materials replaced by <paramref name="materials"/> (written in that order), for
    /// <see cref="GdcWriter.EncodeSequence"/>. Only for a file read with <c>recordTokens</c>.
    /// </summary>
    public List<GdcToken> WithMaterials(IReadOnlyList<StoredMaterial> materials)
    {
        if (_layout.Count < 0)
            throw new InvalidOperationException("The material storage was not read with its values.");

        var tokens = Tokens.ToList();
        var segment = new List<GdcToken> { tokens[_layout.Count] with { Value = (uint)materials.Count } };
        foreach (var material in materials)
        {
            if (material.Amount < 0)
                throw new InvalidDataException("A stored amount cannot be negative.");

            ItemContainerTokens.AddUInt32(segment, 0);                                        // block id
            var lengthIndex = segment.Count;
            segment.Add(new GdcToken(0, GdcTokenKind.UInt32KeyUnchanged, 0));                // block length, set below
            ItemContainerTokens.AddString(segment, material.RecordPath);
            ItemContainerTokens.AddUInt32(segment, (uint)material.Amount);
            segment[lengthIndex] = segment[lengthIndex] with { Value = ItemContainerTokens.SizeOf(segment, lengthIndex + 1, segment.Count) };
            segment.Add(new GdcToken(0, GdcTokenKind.UInt32KeyUnchanged, 0));                // block end marker
        }

        // From the count to the end of the last material.
        var removed = _layout.End - _layout.Count;
        tokens.RemoveRange(_layout.Count, removed);
        tokens.InsertRange(_layout.Count, segment);

        var endToken = _layout.End + segment.Count - removed;
        tokens[_layout.Length] = tokens[_layout.Length] with { Value = ItemContainerTokens.SizeOf(tokens, _layout.Length + 1, endToken) };
        return tokens;
    }
}
