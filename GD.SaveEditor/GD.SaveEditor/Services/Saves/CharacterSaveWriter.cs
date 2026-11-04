using System.IO;

namespace GD.SaveEditor.Services.Saves;

public sealed class CharacterSaveWriter(ICharacterSaveParser parser) : ICharacterSaveWriter
{
    public Task<CharacterSaveResult> SaveAsync(Models.CharacterSave character, byte[] data, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var info = new FileInfo(character.FilePath);
            if (!info.Exists || info.Length != character.FileLength || info.LastWriteTimeUtc != character.FileLastWriteUtc)
                throw new SaveFileChangedException(
                    $"{info.Name} of {character.CharacterName} was changed after it was read (was the game running?). Reload it first.");

            cancellationToken.ThrowIfCancellationRequested();

            // player.gdc.20261104-125000.bak next to the file; the game ignores it.
            var backupPath = $"{info.FullName}.{DateTime.Now:yyyyMMdd-HHmmss}.bak";
            File.Copy(info.FullName, backupPath, overwrite: false);

            // Write a temp file first so a crash mid-write never leaves a broken character.
            var tempPath = info.FullName + ".tmp";
            File.WriteAllBytes(tempPath, data);
            File.Move(tempPath, info.FullName, overwrite: true);

            return new CharacterSaveResult(parser.Parse(info.FullName), backupPath);
        }, cancellationToken);
}
