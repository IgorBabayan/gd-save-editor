using System.IO;
using GD.SaveEditor.Services.GameData.Database;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Services.Saves;

/// <summary>
/// Reads a <c>player.gdc</c> (see <see cref="CharacterFile"/> for the layout) into a <see cref="CharacterSave"/>.
/// The class tag is turned into a name with the game texts of <c>database.db</c>.
/// </summary>
public sealed class CharacterSaveParser(IGameTextProvider gameTexts) : ICharacterSaveParser
{
    public CharacterSave Parse(string filePath)
    {
        var info = new FileInfo(filePath);
        var data = File.ReadAllBytes(filePath);
        var file = CharacterFile.Read(data);

        return new CharacterSave
        {
            FilePath = info.FullName,
            CharacterName = string.IsNullOrWhiteSpace(file.Name)
                ? Path.GetFileName(info.DirectoryName ?? filePath).TrimStart('_')
                : file.Name,
            Level = file.Level,
            ClassTag = file.ClassTag,
            ClassDisplayName = gameTexts.GetText(file.ClassTag),
            Hardcore = file.Hardcore,
            IsMale = file.IsMale,
            ExpansionStatus = file.ExpansionStatus,
            Stats = file.Stats,
            Equipment = file.Equipment,
            Inventory = file.Inventory,
            Money = file.Money,
            Waypoints = file.Waypoints,
            UnlockedDifficulty = file.GreatestDifficulty is { } difficulty && Enum.IsDefined((GameDifficulty)difficulty)
                ? (GameDifficulty)difficulty
                : null,
            CrucibleTributes = file.Tributes,
            DetailsError = file.DetailsError,
            RewriteError = file.RewriteError,
            LastPlayed = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            RawData = data,
            FileLength = info.Length,
            FileLastWriteUtc = info.LastWriteTimeUtc
        };
    }
}
