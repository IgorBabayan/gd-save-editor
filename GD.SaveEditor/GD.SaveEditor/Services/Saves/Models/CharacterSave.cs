namespace GD.SaveEditor.Services.Saves.Models;

/// <summary>A Grim Dawn character read from its <c>player.gdc</c>: the header, progress and equipment.</summary>
public sealed record CharacterSave
{
    /// <summary>Full path of <c>player.gdc</c>.</summary>
    public required string FilePath { get; init; }

    public required string CharacterName { get; init; }

    public required int Level { get; init; }

    /// <summary>Class tag from the save (e.g. <c>tagSkillClassName01</c>); empty before a mastery is chosen.</summary>
    public required string ClassTag { get; init; }

    /// <summary>Class name from the game texts, or <c>null</c> when it is not known (database not generated).</summary>
    public string? ClassDisplayName { get; init; }

    public required bool Hardcore { get; init; }

    public bool IsMale { get; init; }

    /// <summary>Expansions the character was created with (header flag, version 2 saves).</summary>
    public byte ExpansionStatus { get; init; }

    /// <summary>
    /// Solo self-found character. Grim Dawn's character header has no such flag that the editor knows of,
    /// so this stays <c>false</c> until the save format is known to store it.
    /// </summary>
    public bool SoloSelfFound { get; init; }

    /// <summary>Last time the game wrote the file.</summary>
    public DateTimeOffset? LastPlayed { get; init; }

    public DateTime? LastPlayedLocal => LastPlayed?.LocalDateTime;

    /// <summary>Attributes and unspent points; <c>null</c> when that part of the file could not be read.</summary>
    public CharacterStats? Stats { get; init; }

    /// <summary>Equipped items; <c>null</c> when that part of the file could not be read (see <see cref="DetailsError"/>).</summary>
    public CharacterEquipment? Equipment { get; init; }

    /// <summary>
    /// Why the part after the header (stats, equipment) could not be read; <c>null</c> when it was read.
    /// The character is still listed and can be opened.
    /// </summary>
    public string? DetailsError { get; init; }

    /// <summary>File contents as read; written back by the save writer (with a backup).</summary>
    public required byte[] RawData { get; init; }

    /// <summary>Size and time of the file when it was read, to detect that the game wrote it meanwhile.</summary>
    public required long FileLength { get; init; }

    public required DateTime FileLastWriteUtc { get; init; }

    /// <summary>Second line of the character list: "Level 94 Warlord", or "Level 94" while the class is unknown.</summary>
    public string Summary => string.IsNullOrWhiteSpace(ClassDisplayName) ? $"Level {Level}" : $"Level {Level} {ClassDisplayName}";
}
