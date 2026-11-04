namespace GD.SaveEditor.Services.GameData.Database;

/// <summary>A row of <c>records</c>: one record of the .arz databases (an item, an affix, ...).</summary>
/// <param name="Path">Record name, e.g. <c>records/items/gearhead/a01_head001.dbr</c>.</param>
/// <param name="Source">File it was read from (<c>database.arz</c>, <c>GDX1.arz</c>, ...); the last one wins.</param>
/// <param name="Type">Record type from the record table.</param>
/// <param name="Class">The record's <c>Class</c> field (e.g. <c>ArmorProtective_Head</c>).</param>
/// <param name="Template">The record's <c>templateName</c> field.</param>
/// <param name="NameTag">Text tag of the display name (<c>itemNameTag</c>, else <c>description</c>), see <c>tags</c>.</param>
/// <param name="Fields">All fields as a JSON object; fields with more than one value are arrays.</param>
/// <param name="Hash">SHA-1 of type and fields: rows with the same hash need no update.</param>
public sealed record GameRecordRow(
    string Path,
    string Source,
    string Type,
    string? Class,
    string? Template,
    string? NameTag,
    string Fields,
    string Hash);

/// <summary>A row of <c>tags</c>: a localized text, e.g. <c>tagSkillClassName01 = Soldier</c>.</summary>
public sealed record GameTagRow(string Tag, string Text, string Source);

/// <summary>A row of <c>resources</c>: a file inside a resource archive (e.g. an item icon in <c>Items.arc</c>).</summary>
public sealed record GameResourceRow(string Archive, string Path, long Size, long Hash);

/// <summary>A row of <c>source_files</c>: a game file the database was built from.</summary>
/// <param name="Path">Relative path in the game folder, e.g. <c>database/database.arz</c>.</param>
public sealed record GameSourceRow(string Path, string Origin, long Size, DateTime LastWriteUtc, string Sha256);

/// <summary>Everything read from the game files in one generator run; compared with <c>database.db</c>.</summary>
public sealed class GameDataSnapshot
{
    public Dictionary<string, GameRecordRow> Records { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, GameTagRow> Tags { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Resources by <c>archive|path</c>.</summary>
    public Dictionary<string, GameResourceRow> Resources { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<GameSourceRow> Sources { get; } = [];

    /// <summary>
    /// File names that were read in this run. Rows from other sources (e.g. an expansion that could not be obtained
    /// this time) are neither updated nor removed.
    /// </summary>
    public HashSet<string> ProcessedSources { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static string ResourceKey(string archive, string path) => $"{archive}|{path}";
}

/// <summary>Number of added, updated and removed rows of one table in a migration.</summary>
public sealed record TableChanges(int Added, int Updated, int Removed)
{
    public bool IsEmpty => Added == 0 && Updated == 0 && Removed == 0;

    public override string ToString() => $"+{Added} added, ~{Updated} updated, -{Removed} removed";
}

/// <summary>Outcome of <see cref="IGameDatabaseMigrator.Migrate"/>.</summary>
/// <param name="Name">Name of the applied migration; <c>null</c> when the database already held everything.</param>
/// <param name="ScriptPath">The migration's <c>.sql</c> file; <c>null</c> when nothing was applied.</param>
public sealed record GameMigrationResult(
    string? Name,
    string? ScriptPath,
    TableChanges Records,
    TableChanges Tags,
    TableChanges Resources)
{
    public bool WasApplied => Name is not null;
}
