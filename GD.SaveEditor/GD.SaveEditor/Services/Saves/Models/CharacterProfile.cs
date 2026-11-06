namespace GD.SaveEditor.Services.Saves.Models;

/// <summary>Game difficulty as the character info block stores it.</summary>
public enum GameDifficulty : byte
{
    Normal = 0,
    Elite = 1,
    Ultimate = 2
}

/// <summary>Limits of the values the editor writes into the header, the character info and the bio block.</summary>
public static class CharacterLimits
{
    /// <summary>Longest name the editor writes (the game's name field is about this long).</summary>
    public const int MaxNameLength = 32;

    /// <summary>Devotion points a character can have at most (all shrines and the Crucible).</summary>
    public const int MaxDevotionPoints = 55;

    /// <summary>Upper bound of unspent attribute and skill points.</summary>
    public const int MaxPoints = 1_000_000;

    /// <summary>Upper bound of physique, cunning and spirit.</summary>
    public const int MaxAttribute = 1_000_000;

    /// <summary>Upper bound of the Crucible tributes.</summary>
    public const int MaxTributes = int.MaxValue;

    /// <summary>Base health the game gives per point of physique.</summary>
    public const float HealthPerPhysique = 2.5f;

    /// <summary>Base energy the game gives per point of spirit.</summary>
    public const float EnergyPerSpirit = 2f;

    /// <summary>Why <paramref name="name"/> cannot be a character name; <c>null</c> when it can.</summary>
    public static string? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Enter a name.";
        if (name.Length > MaxNameLength)
            return $"The name can have at most {MaxNameLength} characters.";
        if (name != name.Trim())
            return "The name cannot start or end with a space.";
        if (name.Any(c => char.IsControl(c) || char.IsSurrogate(c)))
            return "The name has a character the game cannot show.";
        return null;
    }
}

/// <summary>
/// Changes to the character itself: the header (name, sex), the character info block (unlocked difficulty, Crucible
/// tributes) and the bio block (unspent points, attributes). <c>null</c> values stay as they are in the file.
/// </summary>
/// <param name="Physique">
/// New physique; the base health stored next to it moves with it (<see cref="CharacterLimits.HealthPerPhysique"/>).
/// </param>
/// <param name="Spirit">
/// New spirit; the base energy stored next to it moves with it (<see cref="CharacterLimits.EnergyPerSpirit"/>).
/// </param>
public sealed record ProfileChanges(
    string? Name = null,
    bool? IsMale = null,
    GameDifficulty? UnlockedDifficulty = null,
    uint? CrucibleTributes = null,
    int? AttributePoints = null,
    int? SkillPoints = null,
    int? DevotionPoints = null,
    int? Physique = null,
    int? Cunning = null,
    int? Spirit = null)
{
    public bool IsEmpty =>
        Name is null && IsMale is null && UnlockedDifficulty is null && CrucibleTributes is null
        && AttributePoints is null && SkillPoints is null && DevotionPoints is null
        && Physique is null && Cunning is null && Spirit is null;

    /// <summary>Changes to the info block.</summary>
    public bool ChangesInfo => UnlockedDifficulty is not null || CrucibleTributes is not null;

    /// <summary>Changes to the bio block.</summary>
    public bool ChangesBio =>
        AttributePoints is not null || SkillPoints is not null || DevotionPoints is not null
        || Physique is not null || Cunning is not null || Spirit is not null;
}
