using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using GD.SaveEditor.Services.GameData.Archives;
using GD.SaveEditor.Services.GameData.Database;
using GD.SaveEditor.Services.GameData.Waypoints;
using Microsoft.Data.Sqlite;

namespace GD.SaveEditor.Services.GameData;

/// <summary>
/// Builds <c>database.db</c> from the game files:
/// <list type="number">
/// <item>gets every <see cref="GameFile"/> (GrimTools download, else the game folder);</item>
/// <item>stops early when they are the same files the database was built from;</item>
/// <item>reads the item records of the .arz databases, the texts of <c>Text_EN.arc</c>, the file list of <c>Items.arc</c>
/// and the rift gates (waypoints) of the world maps in the <c>Levels.arc</c> archives of the game folder;</item>
/// <item>lets <see cref="IGameDatabaseMigrator"/> write and apply a migration with only the differences.</item>
/// </list>
/// </summary>
public sealed class DatabaseGenerator(IGameFileProvider fileProvider, IGameDatabaseMigrator migrator) : IDatabaseGenerator
{
    /// <summary>
    /// Records stored in the database: items (with affixes, components, augments) and skills, which item tooltips
    /// name ("+3 to Disintegration", granted skills). The rest of the game database (monsters, ...) is left out.
    /// </summary>
    private static readonly string[] ImportedRecordPrefixes = ["records/items/", "records/skills/"];

    private static readonly JsonWriterOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public Task<DatabaseGenerationResult> RunAsync(
        IProgress<DatabaseGeneratorEvent>? progress,
        CancellationToken cancellationToken = default) =>
        // Reading and hashing hundreds of megabytes must not run on the UI thread.
        Task.Run(() => RunCoreAsync(new Run(progress), cancellationToken), CancellationToken.None);

    private async Task<DatabaseGenerationResult> RunCoreAsync(Run run, CancellationToken cancellationToken)
    {
        try
        {
            // 1. Game files
            var files = new List<ProvidedGameFile>();
            foreach (var file in GameFile.All)
            {
                var step = run.StartStep($"{file.RelativePath} · {file.Description}");
                var (provided, failure) = await fileProvider.GetAsync(file, step, cancellationToken);
                if (provided is null)
                {
                    step.Finish(false, failure);
                    if (file.IsRequired)
                        return run.Fail($"{file.FileName} is required but not available: {failure}.");

                    run.Log(DatabaseGeneratorMessageLevel.Warning, $"{file.FileName} ({file.Description}) skipped: {failure}.");
                    continue;
                }

                step.Finish(true, provided.Origin switch
                {
                    GameFileOrigin.Downloaded => $"Downloaded from {fileProvider.DownloadBaseAddress}",
                    GameFileOrigin.GameFolder => $"From the game folder: {provided.LocalPath}",
                    _ => $"From an earlier download: {provided.LocalPath}"
                });

                if (provided.Origin == GameFileOrigin.GameFolder && !file.GameFolderOnly)
                    run.Log(DatabaseGeneratorMessageLevel.Info, $"{file.FileName}: {provided.FailureReason}; used the game folder instead.");
                else if (provided.Origin == GameFileOrigin.EarlierDownload)
                    run.Log(DatabaseGeneratorMessageLevel.Warning, $"{file.FileName}: {provided.FailureReason}; used an earlier download.");

                files.Add(provided);
            }

            // 2. Same files as last time? Then there is nothing to migrate.
            var sources = ComputeSources(run, files, cancellationToken);
            if (IsUpToDate(sources))
            {
                run.Log(DatabaseGeneratorMessageLevel.Success, "database.db is already up to date: the game files did not change.");
                return run.Result(DatabaseGenerationStatus.Succeeded);
            }

            // 3. Read the game files. The world maps go first: the records of their rift gates are read from the
            //    .arz databases as well (for the gates' names), although they are not stored in the records table.
            var snapshot = new GameDataSnapshot();
            snapshot.Sources.AddRange(sources);
            var gates = ReadLevels(run, files, snapshot, cancellationToken);
            var gateRecords = new Dictionary<string, GateRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (var gate in gates)
                gateRecords.TryAdd(gate.Gate.RecordPath, new GateRecord(null, null, Found: false));

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (file.File.Kind)
                {
                    case GameFileKind.Database:
                        ReadDatabase(run, file, snapshot, gateRecords, cancellationToken);
                        break;
                    case GameFileKind.Text:
                        ReadTexts(run, file, snapshot, cancellationToken);
                        break;
                    case GameFileKind.Resources:
                        ReadResources(run, file, snapshot, cancellationToken);
                        break;
                    case GameFileKind.Levels:
                        // Read above.
                        continue;
                }

                snapshot.ProcessedSources.Add(file.File.FileName);
            }

            if (snapshot.WaypointsRead)
                AddWaypoints(run, gates, gateRecords, snapshot);

            // 4. Migration with only the differences
            var migrateStep = run.StartStep("Migrating database.db");
            var migration = migrator.Migrate(snapshot, migrateStep, cancellationToken);
            migrateStep.Finish(true, migration.WasApplied ? Path.GetFileName(migration.ScriptPath) : "Nothing to migrate");

            run.Log(DatabaseGeneratorMessageLevel.Success, migration.WasApplied
                ? $"Migration {migration.Name} applied: records {migration.Records}; texts {migration.Tags}; resources {migration.Resources}; waypoints {migration.Waypoints}."
                : "database.db already holds everything from the game files: no migration needed.");

            return run.Result(DatabaseGenerationStatus.Succeeded);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            run.Log(DatabaseGeneratorMessageLevel.Warning, "Database generation cancelled. database.db was not changed.");
            return run.Result(DatabaseGenerationStatus.Cancelled);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or SqliteException or OverflowException)
        {
            run.FailOpenSteps(ex.Message);
            return run.Fail(ex.Message);
        }
    }

    #region Sources

    /// <summary>Size, time and SHA-256 of every file; the hash is reused when size and time match the last run.</summary>
    private List<GameSourceRow> ComputeSources(Run run, List<ProvidedGameFile> files, CancellationToken cancellationToken)
    {
        var step = run.StartStep("Checking the game files");
        var previous = migrator.GetSources();
        var sources = new List<GameSourceRow>();

        for (var i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = files[i];
            var info = new FileInfo(file.LocalPath);
            step.Report(((double)i / files.Count, file.File.FileName));

            var sha256 = previous.TryGetValue(file.File.RelativePath, out var known)
                         && known.Size == info.Length && known.LastWriteUtc == info.LastWriteTimeUtc
                ? known.Sha256
                : HashFile(info.FullName, cancellationToken);

            sources.Add(new GameSourceRow(file.File.RelativePath, file.Origin.ToString(), info.Length, info.LastWriteTimeUtc, sha256));
        }

        step.Finish(true, $"{files.Count} files");
        return sources;
    }

    /// <summary>Same importer and the same content of every file as the last migration.</summary>
    private bool IsUpToDate(List<GameSourceRow> sources)
    {
        if (!migrator.IsCurrentImportVersion())
            return false;

        var previous = migrator.GetSources();
        return previous.Count == sources.Count
               && sources.All(s => previous.TryGetValue(s.Path, out var p) && p.Size == s.Size && p.Sha256 == s.Sha256);
    }

    private static string HashFile(string path, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(1 << 20);
        try
        {
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sha.AppendData(buffer, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return Convert.ToHexString(sha.GetHashAndReset());
    }

    #endregion

    #region Readers

    private static void ReadDatabase(
        Run run,
        ProvidedGameFile file,
        GameDataSnapshot snapshot,
        Dictionary<string, GateRecord> gateRecords,
        CancellationToken cancellationToken)
    {
        var step = run.StartStep($"Reading {file.File.FileName}");
        var records = ArzReader.ReadRecords(
            file.LocalPath,
            name => IsImportedRecord(name) || gateRecords.ContainsKey(name),
            new InlineProgress<double>(f => step.Report((f, $"{file.File.FileName}: {f:P0} of the record table"))),
            cancellationToken);

        var imported = 0;
        foreach (var record in records)
        {
            // Expansions are read after the base game: their version of a record wins.
            if (gateRecords.ContainsKey(record.Name))
                gateRecords[record.Name] = new GateRecord(record.GetString("Class"), GateNameTag(record), Found: true);

            if (!IsImportedRecord(record.Name))
                continue;

            var row = ToRow(record, file.File.FileName);
            snapshot.Records[row.Path] = row;
            imported++;
        }

        step.Finish(true, $"{imported:N0} item records");
    }

    #region Waypoints

    /// <summary>Fields of a rift gate record that name it, the best first.</summary>
    private static readonly string[] GateNameFields = ["journalTag", "description", "FileDescription"];

    /// <summary>What the record of a rift gate says about it.</summary>
    private sealed record GateRecord(string? Class, string? NameTag, bool Found);

    /// <summary>A rift gate of a world map with the campaign it was first found in.</summary>
    private sealed record CampaignGate(WorldRiftGate Gate, GameWaypointCampaign Campaign, int Ordinal, string Source);

    /// <summary>
    /// The rift gates of every <c>Levels.arc</c>, base game first: a gate belongs to the first campaign whose world map
    /// has it (each expansion ships the whole world again). Sets <see cref="GameDataSnapshot.WaypointsRead"/> when at
    /// least one archive could be read; one that cannot is reported and its campaign's new gates are missing.
    /// </summary>
    private static List<CampaignGate> ReadLevels(
        Run run, List<ProvidedGameFile> files, GameDataSnapshot snapshot, CancellationToken cancellationToken)
    {
        var levels = files
            .Where(f => f.File.Kind == GameFileKind.Levels)
            .Select(f => (File: f, Campaign: GameWaypointCampaigns.FromLevelsArchive(f.File.RelativePath)))
            .Where(f => f.Campaign is not null)
            .OrderBy(f => f.Campaign)
            .ToList();

        var gates = new List<CampaignGate>();
        if (levels.Count == 0)
        {
            run.Log(DatabaseGeneratorMessageLevel.Warning,
                "No Levels.arc was found in the game folder: the waypoints were not updated. Set the Grim Dawn folder in Settings to get them.");
            return gates;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var anyRead = false;
        foreach (var (file, campaign) in levels)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var step = run.StartStep($"Reading the waypoints of {file.File.RelativePath}");
            step.Report((0, "world001.map"));
            try
            {
                var found = WorldRiftGateReader.Read(file.LocalPath);
                var added = 0;
                for (var i = 0; i < found.Count; i++)
                {
                    if (seen.Add(found[i].Uid))
                    {
                        gates.Add(new CampaignGate(found[i], campaign!.Value, i, file.File.RelativePath));
                        added++;
                    }
                }

                step.Finish(true, $"{found.Count} rift gates, {added} new");
                anyRead = true;
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or OverflowException)
            {
                step.Finish(false, ex.Message);
                run.Log(DatabaseGeneratorMessageLevel.Warning,
                    $"The {GameWaypointCampaigns.Title(campaign!.Value)} waypoints could not be read from {file.File.RelativePath}: {ex.Message}");
            }
        }

        snapshot.WaypointsRead = anyRead;
        if (!anyRead)
            run.Log(DatabaseGeneratorMessageLevel.Warning, "No waypoints could be read: the waypoints in database.db were kept as they were.");
        return gates;
    }

    /// <summary>
    /// The gates that are teleporters (gates whose record has another class, e.g. a portal that is not a waypoint, are
    /// left out) as rows of <see cref="GameDataSnapshot.Waypoints"/>.
    /// </summary>
    private static void AddWaypoints(
        Run run, List<CampaignGate> gates, Dictionary<string, GateRecord> gateRecords, GameDataSnapshot snapshot)
    {
        var skipped = 0;
        foreach (var gate in gates)
        {
            var record = gateRecords.GetValueOrDefault(gate.Gate.RecordPath);
            if (record is { Found: true, Class: { Length: > 0 } recordClass }
                && !recordClass.Contains("Teleport", StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            snapshot.Waypoints[gate.Gate.Uid] = new GameWaypointRow(
                gate.Gate.Uid, gate.Gate.RecordPath, record?.NameTag, (int)gate.Campaign, gate.Ordinal, gate.Source);
        }

        var counts = GameWaypointCampaigns.All
            .Select(c => $"{GameWaypointCampaigns.Title(c)}: {snapshot.Waypoints.Values.Count(w => w.Campaign == (int)c)}");
        run.Log(DatabaseGeneratorMessageLevel.Info,
            $"Waypoints found: {string.Join("; ", counts)}{(skipped > 0 ? $" ({skipped} gates that are not teleporters left out)" : string.Empty)}.");
    }

    /// <summary>The name of a rift gate record: its journal tag, else its description, else its file description.</summary>
    private static string? GateNameTag(ArzRecord record)
    {
        foreach (var field in GateNameFields)
        {
            if (record.GetString(field) is { Length: > 0 } value && value != "???")
                return value;
        }

        return null;
    }

    #endregion

    private static void ReadTexts(Run run, ProvidedGameFile file, GameDataSnapshot snapshot, CancellationToken cancellationToken)
    {
        var step = run.StartStep($"Reading {file.File.FileName}");
        using var archive = ArcReader.Open(file.LocalPath);
        var textFiles = archive.Entries.Where(e => e.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)).ToList();
        var count = 0;

        for (var i = 0; i < textFiles.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = textFiles[i];
            step.Report(((double)i / Math.Max(1, textFiles.Count), entry.Name));

            foreach (var (tag, text) in ParseTextFile(archive.ReadFile(entry)))
            {
                snapshot.Tags[tag] = new GameTagRow(tag, text, file.File.FileName);
                count++;
            }
        }

        step.Finish(true, $"{snapshot.Tags.Count:N0} texts from {textFiles.Count} files");
        if (count == 0)
            run.Log(DatabaseGeneratorMessageLevel.Warning, $"{file.File.FileName} has no texts.");
    }

    private static void ReadResources(Run run, ProvidedGameFile file, GameDataSnapshot snapshot, CancellationToken cancellationToken)
    {
        var step = run.StartStep($"Indexing {file.File.FileName}");
        using var archive = ArcReader.Open(file.LocalPath);
        var archiveName = file.File.FileName;

        for (var i = 0; i < archive.Entries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = archive.Entries[i];
            snapshot.Resources[GameDataSnapshot.ResourceKey(archiveName, entry.Name)] =
                new GameResourceRow(archiveName, entry.Name, entry.DecompressedSize, entry.Hash);

            if (i % 500 == 0)
                step.Report(((double)i / archive.Entries.Count, entry.Name));
        }

        step.Finish(true, $"{archive.Entries.Count:N0} resources");
    }

    private static bool IsImportedRecord(string name) =>
        ImportedRecordPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static GameRecordRow ToRow(ArzRecord record, string source)
    {
        var fields = ToJson(record.Fields);
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(record.Type + "\n" + fields)));

        var nameTag = record.GetString("itemNameTag");
        if (string.IsNullOrEmpty(nameTag) && record.GetString("description") is { } description
                                          && description.StartsWith("tag", StringComparison.OrdinalIgnoreCase))
            nameTag = description;

        return new GameRecordRow(
            record.Name,
            source,
            record.Type,
            record.GetString("Class"),
            record.GetString("templateName"),
            string.IsNullOrEmpty(nameTag) ? null : nameTag,
            fields,
            hash);
    }

    /// <summary>The fields as one JSON object; a field repeated in the record keeps its last value.</summary>
    private static string ToJson(IReadOnlyList<ArzField> fields)
    {
        var last = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < fields.Count; i++)
            last[fields[i].Name] = i;

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, JsonOptions))
        {
            writer.WriteStartObject();
            for (var i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                if (last[field.Name] != i)
                    continue;

                writer.WritePropertyName(field.Name);
                if (field.Values.Count == 1)
                {
                    WriteValue(writer, field.Values[0]);
                }
                else
                {
                    writer.WriteStartArray();
                    foreach (var value in field.Values)
                        WriteValue(writer, value);
                    writer.WriteEndArray();
                }
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteValue(Utf8JsonWriter writer, object value)
    {
        switch (value)
        {
            case int i:
                writer.WriteNumberValue(i);
                break;
            case float f when float.IsFinite(f):
                writer.WriteNumberValue(f);
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            default:
                writer.WriteStringValue(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
        }
    }

    /// <summary><c>tag=text</c> lines of a localization file (UTF-8, or UTF-16 with a byte order mark).</summary>
    private static IEnumerable<(string Tag, string Text)> ParseTextFile(byte[] bytes)
    {
        string content;
        if (bytes is [0xFF, 0xFE, ..])
            content = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        else if (bytes is [0xEF, 0xBB, 0xBF, ..])
            content = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        else
            content = Encoding.UTF8.GetString(bytes);

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("//", StringComparison.Ordinal))
                continue;

            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue;

            var tag = line[..separator].Trim();
            if (tag.Length > 0)
                yield return (tag, line[(separator + 1)..].Trim());
        }
    }

    #endregion

    #region Progress

    /// <summary>Reports straight to the callback, on the calling thread (unlike <see cref="Progress{T}"/>).</summary>
    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    /// <summary>Progress bars and messages of one generator run.</summary>
    private sealed class Run(IProgress<DatabaseGeneratorEvent>? progress)
    {
        private readonly List<DatabaseGeneratorMessage> _messages = [];
        private readonly List<Step> _openSteps = [];
        private int _nextStepId;

        public Step StartStep(string title)
        {
            var step = new Step(this, ++_nextStepId);
            _openSteps.Add(step);
            progress?.Report(new DatabaseGeneratorStepStarted(step.Id, title));
            return step;
        }

        public void Log(DatabaseGeneratorMessageLevel level, string text)
        {
            var message = new DatabaseGeneratorMessage(level, text);
            _messages.Add(message);
            progress?.Report(new DatabaseGeneratorMessageReceived(message));
        }

        public DatabaseGenerationResult Fail(string text)
        {
            Log(DatabaseGeneratorMessageLevel.Error, text);
            return Result(DatabaseGenerationStatus.Failed);
        }

        public DatabaseGenerationResult Result(DatabaseGenerationStatus status) => new(status, _messages.ToList());

        /// <summary>Marks every bar that is still running as stopped (the run failed while it was running).</summary>
        public void FailOpenSteps(string detail)
        {
            foreach (var step in _openSteps.ToList())
                step.Finish(false, detail);
        }

        /// <summary>One progress bar; also the <see cref="IProgress{T}"/> the readers report to.</summary>
        public sealed class Step(Run run, int id) : IProgress<(double Fraction, string Detail)>
        {
            private double _fraction;

            public int Id { get; } = id;

            public void Report((double Fraction, string Detail) value)
            {
                _fraction = Math.Clamp(value.Fraction, 0, 1);
                run.Report(new DatabaseGeneratorStepProgress(Id, _fraction, value.Detail));
            }

            public void Finish(bool completed, string? detail)
            {
                if (!run._openSteps.Remove(this))
                    return;

                run.Report(new DatabaseGeneratorStepFinished(Id, completed ? 1 : _fraction, detail, completed));
            }
        }

        private void Report(DatabaseGeneratorEvent generatorEvent) => progress?.Report(generatorEvent);
    }

    #endregion
}
