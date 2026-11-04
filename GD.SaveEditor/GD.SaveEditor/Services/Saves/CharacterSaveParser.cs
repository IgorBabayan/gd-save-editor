using System.IO;
using GD.SaveEditor.Services.GameData.Database;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Services.Saves;

/// <summary>
/// Reads the header of <c>player.gdc</c>:
/// <code>
/// int32 seed, then encrypted: int32 magic ("GDCX"), int32 version (1 or 2),
/// wstring name, bool male, string classTag, int32 level, bool hardcore, [version 2+: byte expansionStatus]
/// </code>
/// The class tag is turned into a name with the game texts of <c>database.db</c>.
/// </summary>
public sealed class CharacterSaveParser(IGameTextProvider gameTexts) : ICharacterSaveParser
{
    /// <summary>"GDCX" read as a little-endian int32.</summary>
    private const uint GdcMagic = 0x58434447;

    public CharacterSave Parse(string filePath)
    {
        var info = new FileInfo(filePath);
        var data = File.ReadAllBytes(filePath);
        if (data.Length < 16)
            throw new InvalidDataException("The file is too short to be a Grim Dawn character.");

        var reader = new GdcReader(data);
        if (reader.ReadUInt32() != GdcMagic)
            throw new InvalidDataException("Not a Grim Dawn character file (GDCX header missing).");

        var version = reader.ReadInt32();
        if (version < 1)
            throw new InvalidDataException($"Unsupported character file version {version}.");

        var name = reader.ReadWideString();
        var male = reader.ReadBool();
        var classTag = reader.ReadString();
        var level = reader.ReadInt32();
        var hardcore = reader.ReadBool();
        var expansionStatus = version >= 2 ? reader.ReadByte() : (byte)0;

        return new CharacterSave
        {
            FilePath = info.FullName,
            CharacterName = string.IsNullOrWhiteSpace(name) ? Path.GetFileName(info.DirectoryName ?? filePath).TrimStart('_') : name,
            Level = level,
            ClassTag = classTag,
            ClassDisplayName = gameTexts.GetText(classTag),
            Hardcore = hardcore,
            IsMale = male,
            ExpansionStatus = expansionStatus,
            LastPlayed = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            RawData = data,
            FileLength = info.Length,
            FileLastWriteUtc = info.LastWriteTimeUtc
        };
    }
}
