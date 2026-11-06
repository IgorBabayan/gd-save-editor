namespace GD.SaveEditor.Services.Saves.Models;

/// <summary>
/// One entry of the faction list (block 13 of <c>player.gdc</c>).
/// </summary>
/// <param name="Index">Position in the list; the game knows the factions by it (see <see cref="CharacterFactions.TextTag"/>).</param>
/// <param name="Modified">The game changed the value at least once.</param>
/// <param name="Unlocked">The faction is known to the character (shown in the game's faction window).</param>
/// <param name="Value">Reputation: positive for a friendly faction, negative (infamy) for a hostile one.</param>
/// <param name="PositiveBoost">Multiplier of reputation gains (friendly factions).</param>
/// <param name="NegativeBoost">Multiplier of infamy gains (hostile factions).</param>
public sealed record FactionStanding(
    int Index, bool Modified, bool Unlocked, float Value, float PositiveBoost, float NegativeBoost);

/// <summary>A rank of the game's faction window and the reputation it starts at.</summary>
/// <param name="Value">
/// Reputation of the rank's lower bound; infamy ranks are negative, as the save file stores them. Setting a faction to
/// a rank writes this value.
/// </param>
public sealed record FactionTier(string Name, int Value, bool IsHostile);

/// <summary>The reputation of the character with every faction: block 13 of <c>player.gdc</c>.</summary>
public sealed record CharacterFactions(IReadOnlyList<FactionStanding> Factions)
{
    /// <summary>Lowest value the editor writes (beyond Nemesis).</summary>
    public const int MinValue = -25_000;

    /// <summary>Highest value the editor writes (Revered).</summary>
    public const int MaxValue = 25_000;

    /// <summary>First index named by the game texts: index <c>n</c> is <c>tagFactionUser{n - 6}</c>.</summary>
    public const int FirstUserIndex = 6;

    /// <summary>
    /// The ranks from Nemesis to Revered. Friendly: Tolerated 0, Friendly 1,500, Respected 5,000, Honored 10,000,
    /// Revered 25,000; hostile (infamy): Hostile 1, Despised 1,500, Hated 8,000, Nemesis 20,000.
    /// </summary>
    public static IReadOnlyList<FactionTier> Tiers { get; } =
    [
        new("Nemesis", -20_000, true),
        new("Hated", -8_000, true),
        new("Despised", -1_500, true),
        new("Hostile", -1, true),
        new("Tolerated", 0, false),
        new("Friendly", 1_500, false),
        new("Respected", 5_000, false),
        new("Honored", 10_000, false),
        new("Revered", 25_000, false)
    ];

    public static FactionTier Revered => Tiers[^1];

    public static FactionTier Nemesis => Tiers[0];

    public static FactionTier Tolerated => Tiers.First(t => t.Value == 0);

    /// <summary>The rank the game shows for <paramref name="value"/>.</summary>
    public static FactionTier TierOf(double value)
    {
        if (value >= 0)
            return Tiers.Last(t => !t.IsHostile && value >= t.Value);

        // Infamy: the deepest rank whose bound is reached (any infamy at all is "Hostile").
        return Tiers.FirstOrDefault(t => t.IsHostile && value <= t.Value) ?? Tiers.Last(t => t.IsHostile);
    }

    /// <summary>
    /// Names the game gives the first indices itself (index 0 is the character's own faction and index 5 is not used
    /// for the player's reputation); the others come from the game texts (<see cref="TextTag"/>).
    /// </summary>
    public static string? BuiltInName(int index) => index switch
    {
        1 => "Devil's Crossing",
        2 => "Aetherials",
        3 => "Chthonians",
        4 => "Cronley's Gang",
        _ => null
    };

    /// <summary>Tag of the faction's name in the game texts; <c>null</c> for the indices the texts do not name.</summary>
    public static string? TextTag(int index) =>
        index >= FirstUserIndex ? $"tagFactionUser{index - FirstUserIndex}" : null;
}
