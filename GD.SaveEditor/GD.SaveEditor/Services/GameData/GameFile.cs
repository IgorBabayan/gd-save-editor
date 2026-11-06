namespace GD.SaveEditor.Services.GameData;

/// <summary>What a game file holds, which decides how it is read.</summary>
public enum GameFileKind
{
    /// <summary>.arz game database (records).</summary>
    Database,

    /// <summary>.arc archive of localization text files (<c>tag=text</c> lines).</summary>
    Text,

    /// <summary>.arc archive of resources (item icons, ...); only indexed.</summary>
    Resources,

    /// <summary>.arc archive of the game world (<c>Levels.arc</c>); only its rift gates (waypoints) are read.</summary>
    Levels
}

/// <summary>A file of the Grim Dawn installation the database is built from.</summary>
/// <param name="RelativePath">Path in the game folder and on GrimTools, with forward slashes.</param>
/// <param name="Description">What the file holds, shown in the progress dialog.</param>
/// <param name="IsRequired">Without it the database cannot be built; optional files (expansions) only warn.</param>
/// <param name="GameFolderOnly">
/// Read from the Grim Dawn installation only, never downloaded (the level archives are far too large for that).
/// </param>
public sealed record GameFile(string RelativePath, string Description, GameFileKind Kind, bool IsRequired, bool GameFolderOnly = false)
{
    /// <summary>File name, e.g. <c>GDX1.arz</c>; also the <c>source</c> of the rows read from it.</summary>
    public string FileName => RelativePath[(RelativePath.LastIndexOf('/') + 1)..];

    /// <summary>
    /// Files read by the database generator, in override order: an expansion record replaces the base-game
    /// record of the same name.
    /// </summary>
    public static IReadOnlyList<GameFile> All { get; } =
    [
        new("database/database.arz", "Base-game records, including items", GameFileKind.Database, IsRequired: true),
        new("gdx1/database/GDX1.arz", "Ashes of Malmouth", GameFileKind.Database, IsRequired: false),
        new("gdx2/database/GDX2.arz", "Forgotten Gods", GameFileKind.Database, IsRequired: false),
        new("gdx3/database/GDX3.arz", "Fangs of Asterkarn", GameFileKind.Database, IsRequired: false),
        new("resources/Text_EN.arc", "English item names and descriptions", GameFileKind.Text, IsRequired: true),
        // Item, affix and skill names added by the expansions are only in their own text archives.
        new("gdx1/resources/Text_EN.arc", "Ashes of Malmouth texts", GameFileKind.Text, IsRequired: false),
        new("gdx2/resources/Text_EN.arc", "Forgotten Gods texts", GameFileKind.Text, IsRequired: false),
        new("gdx3/resources/Text_EN.arc", "Fangs of Asterkarn texts", GameFileKind.Text, IsRequired: false),
        new("resources/Items.arc", "Item icons and other item resources", GameFileKind.Resources, IsRequired: false),
        // The world map of every campaign: the rift gates (waypoints) and their ids. Each expansion ships the whole world
        // again, so a gate belongs to the first of these archives it appears in (see GameWaypointCampaign).
        new("resources/Levels.arc", "Base campaign waypoints", GameFileKind.Levels, IsRequired: false, GameFolderOnly: true),
        new("gdx1/resources/Levels.arc", "Ashes of Malmouth waypoints", GameFileKind.Levels, IsRequired: false, GameFolderOnly: true),
        new("gdx2/resources/Levels.arc", "Forgotten Gods waypoints", GameFileKind.Levels, IsRequired: false, GameFolderOnly: true),
        new("gdx3/resources/Levels.arc", "Fangs of Asterkarn waypoints", GameFileKind.Levels, IsRequired: false, GameFolderOnly: true)
    ];
}
