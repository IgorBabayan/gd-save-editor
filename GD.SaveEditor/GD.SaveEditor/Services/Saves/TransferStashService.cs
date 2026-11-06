using System.IO;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Services.Saves;

/// <summary>A transfer stash read back from the file that was written, and where the previous file was kept.</summary>
public sealed record TransferStashSaveResult(TransferStash Stash, string BackupPath);

public interface ITransferStashService
{
    /// <summary>
    /// Reads the transfer stash of softcore or hardcore characters in the background. It is looked for in the folder
    /// above <paramref name="saveFolder"/> (<c>save</c> above <c>save\main</c>), then in <paramref name="saveFolder"/>.
    /// A missing or unreadable file is reported in the result, not thrown.
    /// </summary>
    /// <param name="extraFieldsHint">Extra item fields the characters of that mode use, for a stash without items.</param>
    Task<TransferStashLoad> LoadAsync(string? saveFolder, bool hardcore, int? extraFieldsHint, CancellationToken cancellationToken = default);

    /// <summary>
    /// The file contents with every sack holding the items as placed in <paramref name="sacks"/> (see
    /// <see cref="PlacedItem"/>), encrypted like the game does; the file itself is not touched.
    /// </summary>
    /// <exception cref="InvalidDataException">The changes do not fit the file, or the result did not read back as expected.</exception>
    byte[] Apply(TransferStash stash, IReadOnlyList<IReadOnlyList<PlacedItem>> sacks);

    /// <summary>
    /// Writes <paramref name="data"/> into the stash file, keeping the previous file as a timestamped backup next to it,
    /// then reads the written file back.
    /// </summary>
    /// <exception cref="SaveFileChangedException">The file changed since it was read (the game saved it).</exception>
    Task<TransferStashSaveResult> SaveAsync(TransferStash stash, byte[] data, CancellationToken cancellationToken = default);
}

public sealed class TransferStashService : ITransferStashService
{
    public const string SoftcoreFileName = "transfer.gst";
    public const string HardcoreFileName = "transfer.gsh";

    public Task<TransferStashLoad> LoadAsync(
        string? saveFolder, bool hardcore, int? extraFieldsHint, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(saveFolder))
                return new TransferStashLoad(hardcore, null, null, "No save folder is set.");

            var fileName = hardcore ? HardcoreFileName : SoftcoreFileName;
            var folder = Path.GetFullPath(saveFolder);
            var candidates = new[] { Path.GetDirectoryName(folder), folder }
                .Where(f => !string.IsNullOrEmpty(f))
                .Select(f => Path.Combine(f!, fileName))
                .ToList();

            if (candidates.FirstOrDefault(File.Exists) is not { } path)
                return new TransferStashLoad(hardcore, candidates[0], null,
                    $"{fileName} was not found next to the save folder. The game makes it once the shared stash is opened.");

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new TransferStashLoad(hardcore, path, Read(path, hardcore, extraFieldsHint), null);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                return new TransferStashLoad(hardcore, path, null, ex.Message);
            }
        }, cancellationToken);

    public byte[] Apply(TransferStash stash, IReadOnlyList<IReadOnlyList<PlacedItem>> sacks)
    {
        var original = TransferStashFile.Read(stash.RawData, recordTokens: true, stash.ItemExtraFields);
        if (original.Layout is not { } layout)
            throw new InvalidDataException("The sacks could not be found in the transfer stash.");

        GdcWriter.EnsureCoversFile(stash.RawData, original.Tokens);
        var tokens = original.Tokens.ToList();
        var delta = ItemContainerTokens.Rebuild(tokens, layout.Tabs.Select(ContainerTokenLayout.From).ToList(), sacks,
            original.ItemExtraFields, floatCells: true, "shared stash tabs");
        tokens[layout.LengthToken] = tokens[layout.LengthToken] with
        {
            Value = ItemContainerTokens.SizeOf(tokens, layout.LengthToken + 1, layout.EndToken + delta)
        };

        var data = GdcWriter.EncodeSequence(stash.RawData, tokens);
        var written = TransferStashFile.Read(data, recordTokens: true, original.ItemExtraFields);
        ItemContainerTokens.Verify(tokens, written.Tokens, "transfer stash");
        return data;
    }

    public Task<TransferStashSaveResult> SaveAsync(TransferStash stash, byte[] data, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var info = new FileInfo(stash.FilePath);
            if (!info.Exists || info.Length != stash.FileLength || info.LastWriteTimeUtc != stash.FileLastWriteUtc)
                throw new SaveFileChangedException(
                    $"{info.Name} was changed after it was read (was the game running?). Reload it first.");

            cancellationToken.ThrowIfCancellationRequested();

            // transfer.gst.20261106-091500.bak next to the file; the game ignores it.
            var backupPath = $"{info.FullName}.{DateTime.Now:yyyyMMdd-HHmmss}.bak";
            File.Copy(info.FullName, backupPath, overwrite: false);

            // Write a temp file first so a crash mid-write never leaves a broken stash.
            var tempPath = info.FullName + ".tmp";
            File.WriteAllBytes(tempPath, data);
            File.Move(tempPath, info.FullName, overwrite: true);

            return new TransferStashSaveResult(Read(info.FullName, stash.Hardcore, stash.ItemExtraFields), backupPath);
        }, cancellationToken);

    private static TransferStash Read(string path, bool hardcore, int? extraFieldsHint)
    {
        var info = new FileInfo(path);
        var data = File.ReadAllBytes(path);
        var file = TransferStashFile.Read(data, extraFieldsHint: extraFieldsHint);
        return new TransferStash
        {
            FilePath = info.FullName,
            Hardcore = hardcore,
            Version = file.Version,
            Sacks = file.Sacks,
            ItemExtraFields = file.ItemExtraFields,
            RawData = data,
            FileLength = info.Length,
            FileLastWriteUtc = info.LastWriteTimeUtc
        };
    }
}
