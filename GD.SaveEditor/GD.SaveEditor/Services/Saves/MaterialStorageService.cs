using System.IO;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Services.Saves;

/// <summary>A material storage read back from the file that was written, and where the previous file was kept.</summary>
public sealed record MaterialStorageSaveResult(MaterialStorage Storage, string BackupPath);

public interface IMaterialStorageService
{
    /// <summary>
    /// Reads the material storage of softcore or hardcore characters in the background, from the folder above
    /// <paramref name="saveFolder"/> (then <paramref name="saveFolder"/> itself). A missing or unreadable file is
    /// reported in the result, not thrown.
    /// </summary>
    Task<MaterialStorageLoad> LoadAsync(string? saveFolder, bool hardcore, CancellationToken cancellationToken = default);

    /// <summary>
    /// The file contents with the amounts of <paramref name="amounts"/> (by record path) set; the other materials keep
    /// theirs. A material the file does not list yet is added (in the game's order, sorted by path) unless its amount is
    /// 0; a listed one set to 0 stays listed with 0, as the game does. The file itself is not touched.
    /// </summary>
    /// <exception cref="InvalidDataException">An amount is out of range, or the result did not read back as expected.</exception>
    byte[] Apply(MaterialStorage storage, IReadOnlyDictionary<string, int> amounts);

    /// <summary>Writes <paramref name="data"/> into the storage file (keeping a backup), then reads it back.</summary>
    /// <exception cref="SaveFileChangedException">The file changed since it was read (the game saved it).</exception>
    Task<MaterialStorageSaveResult> SaveAsync(MaterialStorage storage, byte[] data, CancellationToken cancellationToken = default);
}

public sealed class MaterialStorageService : IMaterialStorageService
{
    public const string SoftcoreFileName = "reagents.gst";
    public const string HardcoreFileName = "reagents.gsh";

    public Task<MaterialStorageLoad> LoadAsync(string? saveFolder, bool hardcore, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var fileName = hardcore ? HardcoreFileName : SoftcoreFileName;
            var (path, exists) = SharedSaveFiles.Find(saveFolder, fileName);
            if (path is null)
                return new MaterialStorageLoad(hardcore, null, null, "No save folder is set.");
            if (!exists)
                return new MaterialStorageLoad(hardcore, path, null,
                    $"{fileName} was not found next to the save folder. The game (version 1.3 or later) makes it once materials are stored at the Smuggler.");

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new MaterialStorageLoad(hardcore, path, Read(path, hardcore), null);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                return new MaterialStorageLoad(hardcore, path, null, ex.Message);
            }
        }, cancellationToken);

    public byte[] Apply(MaterialStorage storage, IReadOnlyDictionary<string, int> amounts)
    {
        var original = MaterialStorageFile.Read(storage.RawData, recordTokens: true);
        GdcWriter.EnsureCoversFile(storage.RawData, original.Tokens);

        foreach (var (record, amount) in amounts)
        {
            if (amount is < 0 or > MaterialStorage.MaxAmount)
                throw new InvalidDataException($"A stored amount must be from 0 to {MaterialStorage.MaxAmount:N0}.");
            if (!record.StartsWith("records/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"\"{record}\" is not a material record.");
        }

        var materials = original.Materials
            .Select(m => amounts.TryGetValue(m.RecordPath, out var amount) ? m with { Amount = amount } : m)
            .ToList();
        var listed = materials.Select(m => m.RecordPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = amounts
            .Where(a => a.Value > 0 && !listed.Contains(a.Key))
            .Select(a => new StoredMaterial(a.Key, a.Value))
            .OrderBy(m => m.RecordPath, StringComparer.Ordinal)
            .ToList();

        if (added.Count > 0)
        {
            // The game keeps the list sorted by path: new materials go to their place (at the end of a list it did not sort).
            var sorted = materials.Select(m => m.RecordPath).SequenceEqual(
                materials.Select(m => m.RecordPath).OrderBy(p => p, StringComparer.Ordinal));
            materials.AddRange(added);
            if (sorted)
                materials.Sort((a, b) => string.CompareOrdinal(a.RecordPath, b.RecordPath));
        }

        var tokens = original.WithMaterials(materials);
        var data = GdcWriter.EncodeSequence(storage.RawData, tokens);
        var written = MaterialStorageFile.Read(data, recordTokens: true);
        ItemContainerTokens.Verify(tokens, written.Tokens, "material storage");
        return data;
    }

    public Task<MaterialStorageSaveResult> SaveAsync(MaterialStorage storage, byte[] data, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var backup = SharedSaveFiles.WriteWithBackup(storage.FilePath, storage.FileLength, storage.FileLastWriteUtc, data);
            return new MaterialStorageSaveResult(Read(storage.FilePath, storage.Hardcore), backup);
        }, cancellationToken);

    private static MaterialStorage Read(string path, bool hardcore)
    {
        var info = new FileInfo(path);
        var data = File.ReadAllBytes(path);
        var file = MaterialStorageFile.Read(data);
        return new MaterialStorage
        {
            FilePath = info.FullName,
            Hardcore = hardcore,
            Materials = file.Materials,
            RawData = data,
            FileLength = info.Length,
            FileLastWriteUtc = info.LastWriteTimeUtc
        };
    }
}
