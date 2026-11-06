namespace GD.SaveEditor.Services.GameData.Waypoints;

/// <summary>The campaign a waypoint belongs to: the first <c>Levels.arc</c> (base game, then each expansion) that has it.</summary>
public enum GameWaypointCampaign
{
    BaseGame = 0,
    AshesOfMalmouth = 1,
    ForgottenGods = 2,
    FangsOfAsterkarn = 3
}

/// <summary>Display texts of <see cref="GameWaypointCampaign"/>.</summary>
public static class GameWaypointCampaigns
{
    public static IReadOnlyList<GameWaypointCampaign> All { get; } =
    [
        GameWaypointCampaign.BaseGame,
        GameWaypointCampaign.AshesOfMalmouth,
        GameWaypointCampaign.ForgottenGods,
        GameWaypointCampaign.FangsOfAsterkarn
    ];

    public static string Title(GameWaypointCampaign campaign) => campaign switch
    {
        GameWaypointCampaign.BaseGame => "Base campaign (Acts 1–3)",
        GameWaypointCampaign.AshesOfMalmouth => "Ashes of Malmouth",
        GameWaypointCampaign.ForgottenGods => "Forgotten Gods",
        GameWaypointCampaign.FangsOfAsterkarn => "Fangs of Asterkarn",
        _ => campaign.ToString()
    };

    /// <summary>Relative path of the <c>Levels.arc</c> a campaign's waypoints are read from (see <c>GameFile.All</c>).</summary>
    public static string LevelsArchive(GameWaypointCampaign campaign) => campaign switch
    {
        GameWaypointCampaign.BaseGame => "resources/Levels.arc",
        GameWaypointCampaign.AshesOfMalmouth => "gdx1/resources/Levels.arc",
        GameWaypointCampaign.ForgottenGods => "gdx2/resources/Levels.arc",
        GameWaypointCampaign.FangsOfAsterkarn => "gdx3/resources/Levels.arc",
        _ => throw new ArgumentOutOfRangeException(nameof(campaign))
    };

    /// <summary>The campaign of a <c>Levels.arc</c>; <c>null</c> for another file.</summary>
    public static GameWaypointCampaign? FromLevelsArchive(string relativePath)
    {
        foreach (var campaign in All)
        {
            if (string.Equals(LevelsArchive(campaign), relativePath, StringComparison.OrdinalIgnoreCase))
                return campaign;
        }

        return null;
    }
}

/// <summary>A waypoint (rift gate) of the game world.</summary>
/// <param name="Uid">
/// Id of the gate as 32 lowercase hex digits: its 16 bytes in the order the world map and the save file
/// (<c>player.gdc</c>, block 6) store them.
/// </param>
/// <param name="RecordPath">Record of the gate.</param>
/// <param name="Name">Display name from the game texts, else the record's file name.</param>
/// <param name="Order">Position of the gate in its campaign's world map.</param>
public sealed record GameWaypoint(string Uid, string RecordPath, string Name, GameWaypointCampaign Campaign, int Order);
