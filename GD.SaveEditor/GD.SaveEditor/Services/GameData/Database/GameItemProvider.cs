using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace GD.SaveEditor.Services.GameData.Database;

/// <summary>Rarity of an item, which sets its frame color like in the game.</summary>
public enum ItemRarity
{
    Unknown,
    Common,
    Magical,
    Rare,
    Epic,
    Legendary,
    Relic,
    Component,
    Quest
}

/// <summary>What the game database knows about one record (an item, an affix, a component, ...).</summary>
/// <param name="RecordPath">Record path, e.g. <c>records/items/gearhead/a01_head001.dbr</c>.</param>
/// <param name="Name">Display name (item name, or the affix name for affixes); <c>null</c> when it has none.</param>
/// <param name="Class">The record's <c>Class</c>, e.g. <c>ArmorProtective_Head</c>.</param>
/// <param name="BitmapPath">Icon resource, e.g. <c>items/gearhead/bitmaps/a01_head001.tex</c>.</param>
/// <param name="QualityText">Quality word the game puts before the name (e.g. "Mythical"), if any.</param>
/// <param name="StyleText">Style word the game puts before the name, if any.</param>
/// <param name="SetName">Name of the item set the item belongs to, if any.</param>
public sealed record GameItemInfo(
    string RecordPath,
    string? Name,
    string? Class,
    ItemRarity Rarity,
    string? BitmapPath,
    int? LevelRequirement,
    string? QualityText,
    string? StyleText,
    string? SetName);

/// <summary>Item, affix and component records of <c>database.db</c>.</summary>
public interface IGameItemProvider
{
    /// <summary>Whether <c>database.db</c> exists (generated with the database button).</summary>
    bool IsDatabaseAvailable { get; }

    /// <summary>The record <paramref name="recordPath"/>, or <c>null</c> when it is empty or not in the database.</summary>
    GameItemInfo? GetItem(string? recordPath);

    /// <summary>Forgets cached records, e.g. after the database was migrated.</summary>
    void ClearCache();
}

public sealed partial class GameItemProvider(IGameTextProvider texts) : IGameItemProvider
{
    /// <summary>Icon fields, most common first: items, relics (artifacts), components.</summary>
    private static readonly string[] BitmapFields = ["bitmap", "artifactBitmap", "relicBitmap", "shardBitmap"];

    private readonly Dictionary<string, GameItemInfo?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    public bool IsDatabaseAvailable => File.Exists(GameDataPaths.DatabaseFile);

    public GameItemInfo? GetItem(string? recordPath)
    {
        if (string.IsNullOrWhiteSpace(recordPath))
            return null;

        var path = recordPath.Replace('\\', '/');
        lock (_lock)
        {
            if (_cache.TryGetValue(path, out var cached))
                return cached;

            var info = Read(path);
            _cache[path] = info;
            return info;
        }
    }

    public void ClearCache()
    {
        lock (_lock)
            _cache.Clear();
    }

    /// <summary>
    /// Removes the game's formatting from a text: color codes (<c>{^Y}</c>, <c>^Y</c>) and gender variants
    /// (<c>[ms]Name[fs]Name</c>, the first one is kept).
    /// </summary>
    public static string CleanText(string text)
    {
        var genderVariant = GenderVariantRegex().Match(text);
        if (genderVariant.Success)
            text = genderVariant.Groups[1].Value;

        text = ColorCodeRegex().Replace(text, string.Empty);
        return text.Trim();
    }

    private GameItemInfo? Read(string path)
    {
        if (!IsDatabaseAvailable)
            return null;

        string? cls, nameTag, fieldsJson;
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = GameDataPaths.DatabaseFile,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString());
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT class, name_tag, fields FROM records WHERE path = $path;";
            command.Parameters.AddWithValue("$path", path);
            using var row = command.ExecuteReader();
            if (!row.Read())
                return null;

            cls = row.IsDBNull(0) ? null : row.GetString(0);
            nameTag = row.IsDBNull(1) ? null : row.GetString(1);
            fieldsJson = row.IsDBNull(2) ? null : row.GetString(2);
        }
        catch (SqliteException)
        {
            // No records table yet or a damaged database.
            return null;
        }

        JsonElement fields;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrEmpty(fieldsJson) ? "{}" : fieldsJson);
            fields = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            fields = default;
        }

        var name = Text(nameTag) ?? Text(GetString(fields, "lootRandomizerName"));
        var setName = GetString(fields, "itemSetName") is { Length: > 0 } setRecord && GetItem(setRecord) is { } set
            ? set.Name
            : null;

        return new GameItemInfo(
            RecordPath: path,
            Name: name,
            Class: cls,
            Rarity: GetRarity(cls, GetString(fields, "itemClassification")),
            BitmapPath: BitmapFields.Select(f => GetString(fields, f)).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)),
            LevelRequirement: GetInt(fields, "levelRequirement"),
            QualityText: Text(GetString(fields, "itemQualityTag")),
            StyleText: Text(GetString(fields, "itemStyleTag")),
            SetName: setName);
    }

    private string? Text(string? tag) => texts.GetText(tag) is { } text && CleanText(text) is { Length: > 0 } clean ? clean : null;

    private static ItemRarity GetRarity(string? cls, string? classification)
    {
        if (string.Equals(cls, "ItemArtifact", StringComparison.OrdinalIgnoreCase))
            return ItemRarity.Relic;
        if (string.Equals(cls, "ItemRelic", StringComparison.OrdinalIgnoreCase)
            || string.Equals(cls, "ItemEnchantment", StringComparison.OrdinalIgnoreCase))
            return ItemRarity.Component;

        return classification?.ToLowerInvariant() switch
        {
            "common" or "broken" => ItemRarity.Common,
            "magical" => ItemRarity.Magical,
            "rare" => ItemRarity.Rare,
            "epic" => ItemRarity.Epic,
            "legendary" => ItemRarity.Legendary,
            "quest" => ItemRarity.Quest,
            _ => ItemRarity.Unknown
        };
    }

    /// <summary>A string field; for a field with several values, the first non-empty one.</summary>
    private static string? GetString(JsonElement fields, string name)
    {
        if (fields.ValueKind != JsonValueKind.Object || !fields.TryGetProperty(name, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Array)
            value = value.EnumerateArray().FirstOrDefault(v => v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 });

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static int? GetInt(JsonElement fields, string name)
    {
        if (fields.ValueKind != JsonValueKind.Object || !fields.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Array)
            value = value.EnumerateArray().FirstOrDefault();

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var d) => (int)Math.Round(d),
            JsonValueKind.String when int.TryParse(value.GetString(), out var i) => i,
            _ => null
        };
    }

    [GeneratedRegex(@"^\[ms\](.*?)\[fs\]", RegexOptions.IgnoreCase)]
    private static partial Regex GenderVariantRegex();

    [GeneratedRegex(@"\{\^.\}|\^.")]
    private static partial Regex ColorCodeRegex();
}
