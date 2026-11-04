using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;

namespace GD.SaveEditor.Services.GameData.Database;

/// <summary>
/// Keeps <c>database.db</c> in step with the game files through migrations: each run that finds differences writes
/// them as one <c>.sql</c> file into <c>%APPDATA%\GD.SaveEditor\Migrations</c>, holding only what the database does
/// not have yet (new and changed rows, and rows that are gone), and applies it in one transaction.
/// </summary>
public interface IGameDatabaseMigrator
{
    /// <summary>The game files the database was last built from, by relative path; empty when there is no database.</summary>
    IReadOnlyDictionary<string, GameSourceRow> GetSources();

    /// <summary>Whether the database was built by this version of the importer (same records, same JSON).</summary>
    bool IsCurrentImportVersion();

    /// <summary>Creates the database when needed, then writes and applies the migration from it to <paramref name="snapshot"/>.</summary>
    /// <param name="progress">Fraction (0..1) of the migration applied and a short detail text.</param>
    GameMigrationResult Migrate(GameDataSnapshot snapshot, IProgress<(double Fraction, string Detail)>? progress, CancellationToken cancellationToken);
}

public sealed class GameDatabaseMigrator : IGameDatabaseMigrator
{
    /// <summary>Raise when the importer stores records differently, so the next run re-reads the game files.</summary>
    public const int ImportVersion = 2;

    /// <summary><c>PRAGMA user_version</c> of the current schema.</summary>
    private const int SchemaVersion = 1;

    /// <summary>Statements sent to SQLite at once while a migration is applied.</summary>
    private const int StatementsPerBatch = 500;

    private const string ImportVersionKey = "import_version";

    private static readonly string[] SchemaStatements =
    [
        "CREATE TABLE IF NOT EXISTS __migrations (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL UNIQUE, applied_at TEXT NOT NULL, summary TEXT NOT NULL);",
        "CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);",
        "CREATE TABLE IF NOT EXISTS source_files (path TEXT PRIMARY KEY COLLATE NOCASE, origin TEXT NOT NULL, size INTEGER NOT NULL, last_write_utc TEXT NOT NULL, sha256 TEXT NOT NULL);",
        "CREATE TABLE IF NOT EXISTS records (path TEXT PRIMARY KEY COLLATE NOCASE, source TEXT NOT NULL, type TEXT NOT NULL, class TEXT, template TEXT, name_tag TEXT, fields TEXT NOT NULL, hash TEXT NOT NULL);",
        "CREATE INDEX IF NOT EXISTS ix_records_class ON records (class);",
        "CREATE INDEX IF NOT EXISTS ix_records_source ON records (source);",
        "CREATE TABLE IF NOT EXISTS tags (tag TEXT PRIMARY KEY COLLATE NOCASE, text TEXT NOT NULL, source TEXT NOT NULL);",
        "CREATE TABLE IF NOT EXISTS resources (archive TEXT NOT NULL COLLATE NOCASE, path TEXT NOT NULL COLLATE NOCASE, size INTEGER NOT NULL, hash INTEGER NOT NULL, PRIMARY KEY (archive, path));",
        "CREATE VIEW IF NOT EXISTS items AS SELECT r.path, r.class, r.source, r.name_tag, t.text AS name, r.fields FROM records r LEFT JOIN tags t ON t.tag = r.name_tag WHERE r.path LIKE 'records/items/%';",
        $"PRAGMA user_version = {SchemaVersion};"
    ];

    public IReadOnlyDictionary<string, GameSourceRow> GetSources()
    {
        var sources = new Dictionary<string, GameSourceRow>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(GameDataPaths.DatabaseFile))
            return sources;

        try
        {
            using var connection = OpenReadOnly();
            if (!TableExists(connection, "source_files"))
                return sources;

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT path, origin, size, last_write_utc, sha256 FROM source_files;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var row = new GameSourceRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt64(2),
                    DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
                    reader.GetString(4));
                sources[row.Path] = row;
            }
        }
        catch (SqliteException)
        {
            // A damaged database is rebuilt from the game files: report no sources.
            sources.Clear();
        }

        return sources;
    }

    public bool IsCurrentImportVersion()
    {
        if (!File.Exists(GameDataPaths.DatabaseFile))
            return false;

        try
        {
            using var connection = OpenReadOnly();
            if (!TableExists(connection, "meta"))
                return false;

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM meta WHERE key = $key;";
            command.Parameters.AddWithValue("$key", ImportVersionKey);
            return command.ExecuteScalar() is string value
                   && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version)
                   && version == ImportVersion;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    public GameMigrationResult Migrate(GameDataSnapshot snapshot, IProgress<(double Fraction, string Detail)>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(GameDataPaths.ConfigDirectory);
        Directory.CreateDirectory(GameDataPaths.MigrationsDirectory);

        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = GameDataPaths.DatabaseFile,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString());
            connection.Open();

            EnsureSchema(connection, cancellationToken);

            progress?.Report((0, "Comparing with database.db"));
            var name = $"{DateTime.UtcNow:yyyyMMddHHmmss}_game_data";
            var scriptPath = Path.Combine(GameDataPaths.MigrationsDirectory, name + ".sql");

            var plan = CreatePlan(connection, snapshot);
            if (plan.IsEmpty)
            {
                progress?.Report((1, "Nothing to migrate"));
                return new GameMigrationResult(null, null, plan.Records, plan.Tags, plan.Resources);
            }

            var summary = $"records {plan.Records}; tags {plan.Tags}; resources {plan.Resources}";
            WriteScript(scriptPath, name, summary, plan, snapshot);

            progress?.Report((0, $"Applying {Path.GetFileName(scriptPath)}"));
            try
            {
                ApplyScript(connection, scriptPath, plan.StatementCount, progress, cancellationToken);
            }
            catch
            {
                // The database is unchanged (rolled back): drop the script so it is not mistaken for an applied one.
                TryDelete(scriptPath);
                throw;
            }

            progress?.Report((1, $"Applied {Path.GetFileName(scriptPath)}"));
            return new GameMigrationResult(name, scriptPath, plan.Records, plan.Tags, plan.Resources);
        }
        finally
        {
            // Release every handle on database.db so the editor's readers see the new data.
            SqliteConnection.ClearAllPools();
        }
    }

    #region Schema

    private static void EnsureSchema(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "PRAGMA user_version;";
            if (Convert.ToInt32(check.ExecuteScalar(), CultureInfo.InvariantCulture) >= SchemaVersion)
                return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var name = $"{DateTime.UtcNow:yyyyMMddHHmmss}_initial_schema";
        var scriptPath = Path.Combine(GameDataPaths.MigrationsDirectory, name + ".sql");
        var lines = new List<string> { $"-- Migration {name}: creates the tables of database.db." };
        lines.AddRange(SchemaStatements);
        lines.Add(RecordMigrationStatement(name, "initial schema"));
        File.WriteAllLines(scriptPath, lines, new UTF8Encoding(false));

        ApplyScript(connection, scriptPath, SchemaStatements.Length + 1, null, cancellationToken);
    }

    #endregion

    #region Planning

    private sealed record Plan(
        List<GameRecordRow> RecordUpserts,
        List<string> RecordDeletes,
        List<GameTagRow> TagUpserts,
        List<string> TagDeletes,
        List<GameResourceRow> ResourceUpserts,
        List<(string Archive, string Path)> ResourceDeletes,
        List<GameSourceRow> SourceUpserts,
        bool ImportVersionChanged,
        TableChanges Records,
        TableChanges Tags,
        TableChanges Resources)
    {
        public bool IsEmpty => Records.IsEmpty && Tags.IsEmpty && Resources.IsEmpty && SourceUpserts.Count == 0 && !ImportVersionChanged;

        public int StatementCount =>
            RecordUpserts.Count + RecordDeletes.Count + TagUpserts.Count + TagDeletes.Count
            + ResourceUpserts.Count + ResourceDeletes.Count + SourceUpserts.Count + 2;
    }

    /// <summary>Only what the database does not hold yet: new rows, changed rows and rows of read sources that are gone.</summary>
    private static Plan CreatePlan(SqliteConnection connection, GameDataSnapshot snapshot)
    {
        // Records
        var existingRecords = new Dictionary<string, (string Source, string Hash)>(StringComparer.OrdinalIgnoreCase);
        Query(connection, "SELECT path, source, hash FROM records;", r => existingRecords[r.GetString(0)] = (r.GetString(1), r.GetString(2)));

        var recordUpserts = new List<GameRecordRow>();
        var recordsAdded = 0;
        foreach (var row in snapshot.Records.Values)
        {
            if (!existingRecords.TryGetValue(row.Path, out var existing))
            {
                recordUpserts.Add(row);
                recordsAdded++;
            }
            else if (snapshot.ProcessedSources.Contains(existing.Source)
                     && (!string.Equals(existing.Hash, row.Hash, StringComparison.Ordinal)
                         || !string.Equals(existing.Source, row.Source, StringComparison.OrdinalIgnoreCase)))
            {
                recordUpserts.Add(row);
            }
        }

        var recordDeletes = existingRecords
            .Where(e => snapshot.ProcessedSources.Contains(e.Value.Source) && !snapshot.Records.ContainsKey(e.Key))
            .Select(e => e.Key)
            .ToList();

        // Tags
        var existingTags = new Dictionary<string, (string Text, string Source)>(StringComparer.OrdinalIgnoreCase);
        Query(connection, "SELECT tag, text, source FROM tags;", r => existingTags[r.GetString(0)] = (r.GetString(1), r.GetString(2)));

        var tagUpserts = new List<GameTagRow>();
        var tagsAdded = 0;
        foreach (var row in snapshot.Tags.Values)
        {
            if (!existingTags.TryGetValue(row.Tag, out var existing))
            {
                tagUpserts.Add(row);
                tagsAdded++;
            }
            else if (snapshot.ProcessedSources.Contains(existing.Source)
                     && (!string.Equals(existing.Text, row.Text, StringComparison.Ordinal)
                         || !string.Equals(existing.Source, row.Source, StringComparison.OrdinalIgnoreCase)))
            {
                tagUpserts.Add(row);
            }
        }

        var tagDeletes = existingTags
            .Where(e => snapshot.ProcessedSources.Contains(e.Value.Source) && !snapshot.Tags.ContainsKey(e.Key))
            .Select(e => e.Key)
            .ToList();

        // Resources
        var existingResources = new Dictionary<string, (string Archive, string Path, long Size, long Hash)>(StringComparer.OrdinalIgnoreCase);
        Query(connection, "SELECT archive, path, size, hash FROM resources;", r =>
        {
            var archive = r.GetString(0);
            var path = r.GetString(1);
            existingResources[GameDataSnapshot.ResourceKey(archive, path)] = (archive, path, r.GetInt64(2), r.GetInt64(3));
        });

        var resourceUpserts = new List<GameResourceRow>();
        var resourcesAdded = 0;
        foreach (var (key, row) in snapshot.Resources)
        {
            if (!existingResources.TryGetValue(key, out var existing))
            {
                resourceUpserts.Add(row);
                resourcesAdded++;
            }
            else if (existing.Size != row.Size || existing.Hash != row.Hash)
            {
                resourceUpserts.Add(row);
            }
        }

        var resourceDeletes = existingResources
            .Where(e => snapshot.ProcessedSources.Contains(e.Value.Archive) && !snapshot.Resources.ContainsKey(e.Key))
            .Select(e => (e.Value.Archive, e.Value.Path))
            .ToList();

        // Source files
        var existingSources = new Dictionary<string, GameSourceRow>(StringComparer.OrdinalIgnoreCase);
        Query(connection, "SELECT path, origin, size, last_write_utc, sha256 FROM source_files;", r =>
            existingSources[r.GetString(0)] = new GameSourceRow(r.GetString(0), r.GetString(1), r.GetInt64(2),
                DateTime.Parse(r.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
                r.GetString(4)));

        var sourceUpserts = snapshot.Sources
            .Where(s => !existingSources.TryGetValue(s.Path, out var existing) || existing != s)
            .ToList();

        var importVersionChanged = true;
        Query(connection, $"SELECT value FROM meta WHERE key = '{ImportVersionKey}';", r =>
            importVersionChanged = r.GetString(0) != ImportVersion.ToString(CultureInfo.InvariantCulture));

        return new Plan(
            recordUpserts, recordDeletes,
            tagUpserts, tagDeletes,
            resourceUpserts, resourceDeletes,
            sourceUpserts,
            importVersionChanged,
            new TableChanges(recordsAdded, recordUpserts.Count - recordsAdded, recordDeletes.Count),
            new TableChanges(tagsAdded, tagUpserts.Count - tagsAdded, tagDeletes.Count),
            new TableChanges(resourcesAdded, resourceUpserts.Count - resourcesAdded, resourceDeletes.Count));
    }

    #endregion

    #region Script

    /// <summary>Writes the migration as SQL, one statement per line (values never contain line breaks).</summary>
    private static void WriteScript(string path, string name, string summary, Plan plan, GameDataSnapshot snapshot)
    {
        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(false));
        writer.WriteLine($"-- Migration {name}");
        writer.WriteLine($"-- {summary}");
        foreach (var source in snapshot.Sources)
            writer.WriteLine($"-- source: {source.Path} ({source.Origin}, {source.Size} bytes, sha256 {source.Sha256})");

        foreach (var row in plan.RecordUpserts)
            writer.WriteLine(
                $"INSERT OR REPLACE INTO records (path, source, type, class, template, name_tag, fields, hash) VALUES ({Sql(row.Path)}, {Sql(row.Source)}, {Sql(row.Type)}, {Sql(row.Class)}, {Sql(row.Template)}, {Sql(row.NameTag)}, {Sql(row.Fields)}, {Sql(row.Hash)});");
        foreach (var recordPath in plan.RecordDeletes)
            writer.WriteLine($"DELETE FROM records WHERE path = {Sql(recordPath)};");

        foreach (var row in plan.TagUpserts)
            writer.WriteLine($"INSERT OR REPLACE INTO tags (tag, text, source) VALUES ({Sql(row.Tag)}, {Sql(row.Text)}, {Sql(row.Source)});");
        foreach (var tag in plan.TagDeletes)
            writer.WriteLine($"DELETE FROM tags WHERE tag = {Sql(tag)};");

        foreach (var row in plan.ResourceUpserts)
            writer.WriteLine(
                $"INSERT OR REPLACE INTO resources (archive, path, size, hash) VALUES ({Sql(row.Archive)}, {Sql(row.Path)}, {row.Size.ToString(CultureInfo.InvariantCulture)}, {row.Hash.ToString(CultureInfo.InvariantCulture)});");
        foreach (var (archive, resourcePath) in plan.ResourceDeletes)
            writer.WriteLine($"DELETE FROM resources WHERE archive = {Sql(archive)} AND path = {Sql(resourcePath)};");

        foreach (var row in plan.SourceUpserts)
            writer.WriteLine(
                $"INSERT OR REPLACE INTO source_files (path, origin, size, last_write_utc, sha256) VALUES ({Sql(row.Path)}, {Sql(row.Origin)}, {row.Size.ToString(CultureInfo.InvariantCulture)}, {Sql(row.LastWriteUtc.ToString("O", CultureInfo.InvariantCulture))}, {Sql(row.Sha256)});");

        writer.WriteLine($"INSERT OR REPLACE INTO meta (key, value) VALUES ({Sql(ImportVersionKey)}, {Sql(ImportVersion.ToString(CultureInfo.InvariantCulture))});");
        writer.WriteLine(RecordMigrationStatement(name, summary));
    }

    /// <summary>Runs every statement of <paramref name="scriptPath"/> in one transaction (all or nothing).</summary>
    private static void ApplyScript(
        SqliteConnection connection,
        string scriptPath,
        int statementCount,
        IProgress<(double Fraction, string Detail)>? progress,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        var batch = new StringBuilder();
        var batchCount = 0;
        var done = 0;

        void Flush()
        {
            if (batchCount == 0)
                return;

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = batch.ToString();
            command.ExecuteNonQuery();

            done += batchCount;
            batch.Clear();
            batchCount = 0;
            progress?.Report((Math.Min(1, (double)done / Math.Max(1, statementCount)), $"{done:N0} of {statementCount:N0} statements"));
        }

        foreach (var line in File.ReadLines(scriptPath))
        {
            if (line.Length == 0 || line.StartsWith("--", StringComparison.Ordinal))
                continue;

            cancellationToken.ThrowIfCancellationRequested();
            batch.AppendLine(line);
            if (++batchCount >= StatementsPerBatch)
                Flush();
        }

        Flush();
        transaction.Commit();
    }

    private static string RecordMigrationStatement(string name, string summary) =>
        $"INSERT INTO __migrations (name, applied_at, summary) VALUES ({Sql(name)}, {Sql(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture))}, {Sql(summary)});";

    /// <summary>SQL literal; line breaks and NULs are replaced so every statement stays on one line.</summary>
    private static string Sql(string? value)
    {
        if (value is null)
            return "NULL";

        var builder = new StringBuilder(value.Length + 2).Append('\'');
        foreach (var c in value)
        {
            switch (c)
            {
                case '\'':
                    builder.Append("''");
                    break;
                case '\r' or '\n' or '\0':
                    builder.Append(' ');
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.Append('\'').ToString();
    }

    #endregion

    #region Helpers

    private static SqliteConnection OpenReadOnly()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = GameDataPaths.DatabaseFile,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    private static bool TableExists(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", table);
        return command.ExecuteScalar() is not null;
    }

    private static void Query(SqliteConnection connection, string sql, Action<SqliteDataReader> read)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read())
            read(reader);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind; it is not recorded in __migrations.
        }
    }

    #endregion
}
