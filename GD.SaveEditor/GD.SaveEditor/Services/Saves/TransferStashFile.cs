using System.IO;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Services.Saves;

/// <summary>
/// Everything the editor reads from the transfer (shared) stash, <c>transfer.gst</c> (softcore) or
/// <c>transfer.gsh</c> (hardcore), in the folder above the character folders. It is encrypted like <c>player.gdc</c>
/// (see <see cref="GdcReader"/>):
/// <code>
/// int32 seed, then encrypted: int32 2,
/// block 18: int32 version, int32 (key unchanged), string mod, [version 5+: byte expansionStatus],
///   int32 sackCount, sackCount x stash tab (see CharacterFile.ReadStashTab)
/// </code>
/// Newer game versions add int32 fields at the end of every item, like in the inventory; their number is found the
/// same way (see <see cref="CharacterFile"/>): the one that reads every sack exactly to its end.
/// </summary>
public sealed class TransferStashFile
{
    private const uint Magic = 2;
    private const uint BlockId = 18;

    /// <summary>Sacks the editor accepts, so a misread count fails fast.</summary>
    private const int MaxSacks = 64;

    private TransferStashFile()
    {
    }

    public int Version { get; private set; }

    /// <summary>Mod the stash belongs to; empty for the base game.</summary>
    public string Mod { get; private set; } = string.Empty;

    public IReadOnlyList<StashTab> Sacks { get; private set; } = [];

    /// <summary>int32 fields stored after the stack count of every item.</summary>
    public int ItemExtraFields { get; private set; }

    /// <summary>
    /// <see cref="ItemExtraFields"/> was found from the items (some sack holds one); otherwise it was taken from the
    /// hint or the stash version.
    /// </summary>
    public bool IsItemLayoutKnown { get; private set; }

    /// <summary>Every value of the file in order; empty unless read with <c>recordTokens</c>.</summary>
    public IReadOnlyList<GdcToken> Tokens { get; private set; } = [];

    /// <summary>Where the sacks are in <see cref="Tokens"/>; <c>null</c> unless read with <c>recordTokens</c>.</summary>
    public StashTokenLayout? Layout { get; private set; }

    /// <summary>Reads a transfer stash.</summary>
    /// <param name="recordTokens">Keep every value (<see cref="Tokens"/>) to encrypt the file again.</param>
    /// <param name="extraFieldsHint">
    /// Extra item fields to assume when no sack holds an item (the number the characters of the same game version
    /// use); without it the one expected for the stash version is taken.
    /// </param>
    /// <exception cref="InvalidDataException">Not a transfer stash, or one with a layout this editor does not know.</exception>
    public static TransferStashFile Read(byte[] data, bool recordTokens = false, int? extraFieldsHint = null)
    {
        if (data.Length < 12)
            throw new InvalidDataException("The file is too short to be a Grim Dawn transfer stash.");

        var reader = new GdcReader(data, recordTokens);
        if (reader.ReadUInt32() != Magic)
            throw new InvalidDataException("Not a Grim Dawn transfer stash.");

        var blockToken = reader.TokenCount;
        var (id, end) = reader.ReadBlockStart();
        if (id != BlockId)
            throw new InvalidDataException($"Not a Grim Dawn transfer stash (block {id}).");

        var file = new TransferStashFile { Version = reader.ReadInt32() };
        if (file.Version < 4)
            throw new InvalidDataException($"Transfer stash version {file.Version} is not supported.");

        reader.ReadUInt32KeyUnchanged();
        file.Mod = reader.ReadString();
        if (file.Version >= 5)
            reader.ReadByte(); // expansion status

        var count = reader.ReadInt32();
        if (count is < 0 or > MaxSacks)
            throw new InvalidDataException($"Invalid number of transfer stash sacks ({count}, version {file.Version}).");

        var sacksStart = reader.Snapshot();
        foreach (var extraFields in Candidates(file.Version, extraFieldsHint))
        {
            reader.Restore(sacksStart);
            if (TryReadSacks(reader, count, extraFields, end) is not { } result)
                continue;

            var (sacks, layouts) = result;

            var endToken = reader.TokenCount;
            reader.ReadBlockEnd(end);
            if (reader.Position != data.Length)
                throw new InvalidDataException("The transfer stash has data after its sacks this editor does not know.");

            file.Sacks = sacks;
            file.ItemExtraFields = extraFields;
            file.IsItemLayoutKnown = sacks.Any(s => s.Items.Count > 0);
            file.Tokens = reader.Tokens;
            if (recordTokens)
                file.Layout = new StashTokenLayout(blockToken + 1, endToken, layouts);
            return file;
        }

        throw new InvalidDataException($"Transfer stash version {file.Version} has an item layout this editor does not know.");
    }

    /// <summary>The sacks read with <paramref name="extraFields"/> per item; <c>null</c> when they do not fit the block.</summary>
    private static (List<StashTab> Sacks, List<StashTabTokenLayout> Layouts)? TryReadSacks(
        GdcReader reader, int count, int extraFields, int end)
    {
        var sacks = new List<StashTab>(count);
        var layouts = new List<StashTabTokenLayout>(count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                var (sack, layout) = CharacterFile.ReadStashTab(reader, extraFields);
                sacks.Add(sack);
                layouts.Add(layout);
            }
        }
        catch (InvalidDataException)
        {
            // Wrong field count: the caller tries the next one from the same position.
            return null;
        }

        return reader.Position == end ? (sacks, layouts) : null;
    }

    /// <summary>Extra item fields to try: the hint, then the one known for the version, then the others.</summary>
    private static IEnumerable<int> Candidates(int version, int? hint)
    {
        var tried = new HashSet<int>();
        if (hint is { } value && tried.Add(value))
            yield return value;

        // Version 5 is the stash of the game before Fangs of Asterkarn (no extra fields).
        foreach (var count in CharacterFile.ExtraItemFieldCandidates(version >= 6 ? 11 : 4))
        {
            if (tried.Add(count))
                yield return count;
        }
    }
}
