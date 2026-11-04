using System.IO;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Services.Saves;

public sealed class SaveFolderLoader(ISaveFolderScanner scanner, ICharacterSaveParser parser) : ISaveFolderLoader
{
    public Task<SaveFolderContent> LoadAsync(string saveFolder, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var characters = new List<CharacterSave>();
            var errors = new List<SaveFileError>();

            foreach (var file in scanner.FindCharacterFiles(saveFolder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    characters.Add(parser.Parse(file));
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    errors.Add(new SaveFileError(file, ex.Message));
                }
            }

            return new SaveFolderContent(characters, errors);
        }, cancellationToken);

    public Task<CharacterSave> LoadCharacterAsync(string filePath, CancellationToken cancellationToken = default) =>
        Task.Run(() => parser.Parse(filePath), cancellationToken);
}
