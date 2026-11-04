using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using GD.SaveEditor.Services.GameData.Database;
using Microsoft.Data.Sqlite;

namespace GD.SaveEditor.Services.GameData.Items;

/// <summary>An item record the item builder offers: something that can be put in a bag.</summary>
/// <param name="RecordPath">E.g. <c>records/items/gearhead/a01_head001.dbr</c>.</param>
/// <param name="Name">Name as the game shows it (style and quality words included).</param>
/// <param name="TypeLabel">"Helm", "Two-Handed Sword", "Component", ... (the group in the builder).</param>
/// <param name="GroupOrder">Order of <paramref name="TypeLabel"/> groups, see <see cref="ItemClasses.GetGroupOrder"/>.</param>
/// <param name="BitmapPath">Icon resource; every offered item has one (its size gives the item's size in a bag).</param>
/// <param name="SetName">Name of the item set the item belongs to, if any.</param>
public sealed record CatalogItem(
    string RecordPath,
    string Name,
    string? Class,
    ItemCategory Category,
    string TypeLabel,
    int GroupOrder,
    ItemRarity Rarity,
    string BitmapPath,
    int LevelRequirement,
    string? SetName)
{
    public bool IsStackable => ItemClasses.IsStackable(Category);
}

/// <summary>What can be added to an item, by the folder its records are in.</summary>
public enum ItemPartKind
{
    /// <summary><c>records/items/lootaffixes/prefix/</c>.</summary>
    Prefix,

    /// <summary><c>records/items/lootaffixes/suffix/</c>.</summary>
    Suffix,

    /// <summary>Blacksmith bonus, <c>records/items/lootaffixes/crafting/</c>.</summary>
    CraftingBonus,

    /// <summary>Completion bonus of a relic (or of a component that has one), <c>records/items/lootaffixes/completionrelics/</c>.</summary>
    CompletionBonus,

    /// <summary>Components (<c>ItemRelic</c>) slotted into gear.</summary>
    Component,

    /// <summary>Augments (<c>ItemEnchantment</c>) applied to gear.</summary>
    Augment
}

/// <summary>An affix, bonus, component or augment the item builder offers for a part of an item.</summary>
/// <param name="Name">The game's name; affixes without one (crafting and completion bonuses) are named by their first stat.</param>
/// <param name="Summary">Stats of the record on one line ("+24 Physique, +8% Health").</param>
/// <param name="Code">File name of the record, which tells records with the same name apart.</param>
public sealed record CatalogPart(
    string RecordPath,
    ItemPartKind Kind,
    string Name,
    ItemRarity Rarity,
    int LevelRequirement,
    string Summary,
    string Code);

/// <summary>The items and parts of <c>database.db</c> the item builder offers.</summary>
public interface IItemCatalog
{
    /// <summary>Whether <c>database.db</c> exists (generated with the database button).</summary>
    bool IsAvailable { get; }

    /// <summary>Every item that can be put in a bag (it has a name and an icon). Read once, then kept until the database changes.</summary>
    IReadOnlyList<CatalogItem> GetItems();

    /// <summary>The item <paramref name="recordPath"/>, or <c>null</c> when the catalog does not offer it.</summary>
    CatalogItem? FindItem(string? recordPath);

    /// <summary>Every record of the kind, ordered by name, then level.</summary>
    IReadOnlyList<CatalogPart> GetParts(ItemPartKind kind);

    /// <summary>
    /// Completion bonuses the game rolls for <paramref name="recordPath"/> (a relic, or a component with a bonus
    /// table): the records its <c>bonusTableName</c> lists. Empty when it has no such table.
    /// </summary>
    IReadOnlyList<CatalogPart> GetCompletionBonuses(string recordPath);
}

/// <summary>
/// Reads the offered items and parts from <c>database.db</c> in one query each (names and icon paths come from the
/// JSON fields with <c>json_extract</c>, the texts from the <c>tags</c> table). The lists are kept until the database
/// file changes (it is regenerated with the database button).
/// </summary>
public sealed partial class ItemCatalog(IGameItemProvider items) : IItemCatalog
{
    private const string AffixFolder = "records/items/lootaffixes/";

    private readonly Lock _lock = new();
    private DateTime _databaseTime;
    private IReadOnlyList<CatalogItem>? _items;
    private Dictionary<string, CatalogItem>? _itemsByPath;
    private readonly Dictionary<ItemPartKind, IReadOnlyList<CatalogPart>> _parts = [];

    public bool IsAvailable => File.Exists(GameDataPaths.DatabaseFile);

    public IReadOnlyList<CatalogItem> GetItems()
    {
        lock (_lock)
        {
            EnsureCurrent();
            if (_items is null)
            {
                _items = ReadItems();
                _itemsByPath = _items
                    .GroupBy(i => i.RecordPath, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            }

            return _items;
        }
    }

    public CatalogItem? FindItem(string? recordPath)
    {
        if (string.IsNullOrWhiteSpace(recordPath))
            return null;

        GetItems();
        lock (_lock)
            return _itemsByPath?.GetValueOrDefault(recordPath.Replace('\\', '/'));
    }

    public IReadOnlyList<CatalogPart> GetParts(ItemPartKind kind)
    {
        lock (_lock)
        {
            EnsureCurrent();
            if (!_parts.TryGetValue(kind, out var parts))
                _parts[kind] = parts = ReadParts(kind);
            return parts;
        }
    }

    public IReadOnlyList<CatalogPart> GetCompletionBonuses(string recordPath)
    {
        if (items.GetItem(recordPath) is not { } item
            || ItemStatBlock.GetString(item.Fields, "bonusTableName") is not { Length: > 0 } tablePath
            || items.GetItem(tablePath) is not { Fields.ValueKind: JsonValueKind.Object } table)
            return [];

        var known = GetParts(ItemPartKind.CompletionBonus).ToDictionary(p => p.RecordPath, StringComparer.OrdinalIgnoreCase);
        var bonuses = new List<CatalogPart>();
        foreach (var field in table.Fields.EnumerateObject())
        {
            if (!RandomizerNameRegex().IsMatch(field.Name))
                continue;

            foreach (var path in Strings(field.Value))
            {
                var normalized = path.Replace('\\', '/');
                if (known.TryGetValue(normalized, out var part) && !bonuses.Contains(part))
                    bonuses.Add(part);
                else if (!known.ContainsKey(normalized) && items.GetItem(normalized) is { } record)
                    bonuses.Add(ToPart(normalized, ItemPartKind.CompletionBonus, record.Name, record.Rarity, record.LevelRequirement ?? 0, record.Fields));
            }
        }

        return bonuses;
    }

    /// <summary>Forgets the lists when <c>database.db</c> was written since they were read.</summary>
    private void EnsureCurrent()
    {
        var time = File.Exists(GameDataPaths.DatabaseFile) ? File.GetLastWriteTimeUtc(GameDataPaths.DatabaseFile) : default;
        if (time == _databaseTime)
            return;

        _databaseTime = time;
        _items = null;
        _itemsByPath = null;
        _parts.Clear();
    }

    #region Items

    private static IReadOnlyList<CatalogItem> ReadItems()
    {
        var result = new List<CatalogItem>();
        if (!File.Exists(GameDataPaths.DatabaseFile))
            return result;

        const string sql = """
            SELECT r.path, r.class, tn.text,
                   json_extract(r.fields, '$.itemClassification'),
                   json_extract(r.fields, '$.levelRequirement'),
                   json_extract(r.fields, '$.bitmap'),
                   json_extract(r.fields, '$.artifactBitmap'),
                   json_extract(r.fields, '$.relicBitmap'),
                   json_extract(r.fields, '$.shardBitmap'),
                   tq.text, ts.text, tset.text
            FROM records r
            LEFT JOIN tags tn ON tn.tag = r.name_tag
            LEFT JOIN tags tq ON tq.tag = json_extract(r.fields, '$.itemQualityTag')
            LEFT JOIN tags ts ON ts.tag = json_extract(r.fields, '$.itemStyleTag')
            LEFT JOIN records rset ON rset.path = json_extract(r.fields, '$.itemSetName')
            LEFT JOIN tags tset ON tset.tag = rset.name_tag
            WHERE r.path LIKE 'records/items/%' AND r.path NOT LIKE 'records/items/lootaffixes/%' AND r.name_tag IS NOT NULL;
            """;

        try
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            using var row = command.ExecuteReader();
            while (row.Read())
            {
                var path = row.GetString(0);
                var cls = row.IsDBNull(1) ? null : row.GetString(1);
                if (ItemClasses.GetCategory(cls) is not { } category)
                    continue;

                var baseName = Text(row, 2);
                var bitmap = FirstString(row, 5, 6, 7, 8);
                if (baseName is null || bitmap is null)
                    continue;

                result.Add(new CatalogItem(
                    path,
                    ComposeName(baseName, Text(row, 9), Text(row, 10)),
                    cls,
                    category,
                    ItemClasses.GetLabel(cls) ?? CategoryLabel(category),
                    ItemClasses.GetGroupOrder(cls) + (int)category * 100,
                    GetRarity(cls, FirstString(row, 3)),
                    bitmap,
                    (int)Math.Round(Number(row, 4) ?? 0),
                    Text(row, 11)));
            }
        }
        catch (SqliteException)
        {
            // No records table yet or a damaged database: nothing to offer.
        }

        return result
            .OrderBy(i => i.GroupOrder)
            .ThenBy(i => i.TypeLabel, StringComparer.Ordinal)
            .ThenBy(i => i.LevelRequirement)
            .ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string CategoryLabel(ItemCategory category) => category switch
    {
        ItemCategory.Consumable => "Consumable",
        ItemCategory.Component => "Component",
        ItemCategory.Augment => "Augment",
        ItemCategory.Relic => "Relic",
        _ => "Other"
    };

    /// <summary>The game's naming: style, quality, base name (the quality is left out when the name starts with it).</summary>
    private static string ComposeName(string baseName, string? quality, string? style)
    {
        var parts = new List<string>(3);
        if (style is not null) parts.Add(style);
        if (quality is not null && !baseName.StartsWith(quality, StringComparison.OrdinalIgnoreCase)) parts.Add(quality);
        parts.Add(baseName);
        return string.Join(' ', parts);
    }

    private static ItemRarity GetRarity(string? cls, string? classification)
    {
        switch (ItemClasses.GetCategory(cls))
        {
            case ItemCategory.Relic:
                return ItemRarity.Relic;
            case ItemCategory.Component or ItemCategory.Augment:
                return ItemRarity.Component;
        }

        return classification?.ToLowerInvariant() switch
        {
            "common" or "broken" => ItemRarity.Common,
            "magical" => ItemRarity.Magical,
            "rare" => ItemRarity.Rare,
            "epic" => ItemRarity.Epic,
            "legendary" => ItemRarity.Legendary,
            "quest" => ItemRarity.Quest,
            _ => cls == "QuestItem" ? ItemRarity.Quest : ItemRarity.Common
        };
    }

    #endregion

    #region Parts

    private IReadOnlyList<CatalogPart> ReadParts(ItemPartKind kind)
    {
        var result = new List<CatalogPart>();
        if (!File.Exists(GameDataPaths.DatabaseFile))
            return result;

        var filter = kind switch
        {
            ItemPartKind.Prefix => $"r.path LIKE '{AffixFolder}prefix/%'",
            ItemPartKind.Suffix => $"r.path LIKE '{AffixFolder}suffix/%'",
            ItemPartKind.CraftingBonus => $"r.path LIKE '{AffixFolder}crafting/%'",
            ItemPartKind.CompletionBonus => $"r.path LIKE '{AffixFolder}completionrelics/%'",
            ItemPartKind.Component => "r.class = 'ItemRelic'",
            _ => "r.class = 'ItemEnchantment'"
        };

        // Affix records name themselves in lootRandomizerName; components and augments through their name tag.
        // Loot tables in the affix folders (LootRandomizerTable) are not affixes.
        var sql = $"""
            SELECT r.path, COALESCE(tn.text, tr.text), json_extract(r.fields, '$.itemClassification'),
                   json_extract(r.fields, '$.levelRequirement'), r.fields
            FROM records r
            LEFT JOIN tags tn ON tn.tag = r.name_tag
            LEFT JOIN tags tr ON tr.tag = json_extract(r.fields, '$.lootRandomizerName')
            WHERE {filter} AND (r.class IS NULL OR r.class NOT LIKE '%Table%');
            """;

        try
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            using var row = command.ExecuteReader();
            while (row.Read())
            {
                JsonElement fields;
                try
                {
                    using var document = JsonDocument.Parse(row.IsDBNull(4) ? "{}" : row.GetString(4));
                    fields = document.RootElement.Clone();
                }
                catch (JsonException)
                {
                    continue;
                }

                var rarity = kind is ItemPartKind.Component or ItemPartKind.Augment
                    ? ItemRarity.Component
                    : GetRarity(null, FirstString(row, 2));
                result.Add(ToPart(row.GetString(0), kind, Text(row, 1), rarity, (int)Math.Round(Number(row, 3) ?? 0), fields));
            }
        }
        catch (SqliteException)
        {
            // No records table yet or a damaged database: nothing to offer.
        }

        return result
            // A record without a name and without stats this editor can show would be a blank line.
            .Where(p => p.Summary.Length > 0 || !string.Equals(p.Name, p.Code, StringComparison.Ordinal))
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.LevelRequirement)
            .ThenBy(p => p.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private CatalogPart ToPart(string path, ItemPartKind kind, string? name, ItemRarity rarity, int level, JsonElement fields)
    {
        var stats = new ItemStatBlock();
        stats.Add(fields, useLastValue: kind is ItemPartKind.Component or ItemPartKind.Augment or ItemPartKind.CompletionBonus);
        var lines = stats.GetLines().Select(l => l.Text).ToList();
        lines.AddRange(SkillLines(fields));
        var code = Path.GetFileNameWithoutExtension(path);

        return new CatalogPart(
            path,
            kind,
            name is { Length: > 0 } ? GameItemProvider.CleanText(name) : lines.FirstOrDefault() ?? code,
            rarity,
            level,
            string.Join(", ", lines),
            code);
    }

    /// <summary>"+2 to Ring of Steel", "+1 to all skills in Soldier", "+1 to all Skills", "Grants Skill: ...".</summary>
    private IEnumerable<string> SkillLines(JsonElement fields)
    {
        if (ItemStatBlock.GetNumber(fields, "augmentAllLevel") is { } all and > 0)
            yield return $"+{ItemStatBlock.Number(all)} to all Skills";

        for (var i = 1; i <= 2; i++)
        {
            if (ItemStatBlock.GetString(fields, $"augmentMasteryName{i}") is { Length: > 0 } mastery
                && ItemStatBlock.GetNumber(fields, $"augmentMasteryLevel{i}") is { } level and > 0)
                yield return $"+{ItemStatBlock.Number(level)} to all skills in {SkillName(mastery)}";
        }

        for (var i = 1; i <= 4; i++)
        {
            if (ItemStatBlock.GetString(fields, $"augmentSkillName{i}") is { Length: > 0 } skill
                && ItemStatBlock.GetNumber(fields, $"augmentSkillLevel{i}") is { } level and > 0)
                yield return $"+{ItemStatBlock.Number(level)} to {SkillName(skill)}";
        }

        if (ItemStatBlock.GetString(fields, "itemSkillName") is { Length: > 0 } granted)
            yield return $"Grants Skill: {SkillName(granted)}";
    }

    private string SkillName(string record) => items.GetItem(record)?.Name ?? Path.GetFileNameWithoutExtension(record);

    #endregion

    #region Helpers

    private static SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = GameDataPaths.DatabaseFile,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        connection.Open();
        return connection;
    }

    private static string? Text(SqliteDataReader row, int column) =>
        row.IsDBNull(column) ? null : GameItemProvider.CleanText(row.GetString(column)) is { Length: > 0 } text ? text : null;

    /// <summary>
    /// The first non-empty string of the columns. <c>json_extract</c> gives a field with several values as a JSON
    /// array; its first non-empty string counts.
    /// </summary>
    private static string? FirstString(SqliteDataReader row, params int[] columns)
    {
        foreach (var column in columns)
        {
            if (row.IsDBNull(column) || row.GetValue(column) is not string value || value.Length == 0)
                continue;

            if (!value.StartsWith('['))
                return value;

            try
            {
                using var document = JsonDocument.Parse(value);
                if (Strings(document.RootElement).FirstOrDefault() is { } first)
                    return first;
            }
            catch (JsonException)
            {
                return value;
            }
        }

        return null;
    }

    private static double? Number(SqliteDataReader row, int column) => row.IsDBNull(column) ? null : row.GetValue(column) switch
    {
        long l => (double?)l,
        double d => d,
        string s when double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) => d,
        string s when s.StartsWith('[') => FirstNumber(s),
        _ => null
    };

    private static double? FirstNumber(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            foreach (var value in document.RootElement.EnumerateArray())
            {
                if (value.ValueKind == JsonValueKind.Number)
                    return value.GetDouble();
            }
        }
        catch (JsonException)
        {
            // Not a list of numbers.
        }

        return null;
    }

    /// <summary>The non-empty strings of a JSON value (a string, or an array of them).</summary>
    private static IEnumerable<string> Strings(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } single)
        {
            yield return single;
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } text)
                    yield return text;
            }
        }
    }

    [GeneratedRegex(@"^randomizerName\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex RandomizerNameRegex();

    #endregion
}
