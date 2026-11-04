using System.IO;
using Microsoft.Data.Sqlite;

namespace GD.SaveEditor.Services.GameData.Database;

/// <summary>Localized game texts (the <c>tags</c> table of <c>database.db</c>), e.g. class names.</summary>
public interface IGameTextProvider
{
    /// <summary>Text of <paramref name="tag"/>, or <c>null</c> when the database is missing or does not have it.</summary>
    string? GetText(string? tag);

    /// <summary>Forgets cached texts, e.g. after the database was migrated.</summary>
    void ClearCache();
}

public sealed class GameTextProvider : IGameTextProvider
{
    private readonly Dictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    public string? GetText(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        lock (_lock)
        {
            if (_cache.TryGetValue(tag, out var cached))
                return cached;

            var text = Read(tag);
            _cache[tag] = text;
            return text;
        }
    }

    public void ClearCache()
    {
        lock (_lock)
            _cache.Clear();
    }

    private static string? Read(string tag)
    {
        if (!File.Exists(GameDataPaths.DatabaseFile))
            return null;

        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = GameDataPaths.DatabaseFile,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString());
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT text FROM tags WHERE tag = $tag;";
            command.Parameters.AddWithValue("$tag", tag);
            return command.ExecuteScalar() as string;
        }
        catch (SqliteException)
        {
            // No tags table yet (database not generated) or a damaged database: no text.
            return null;
        }
    }
}
