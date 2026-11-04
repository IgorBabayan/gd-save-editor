using System.Globalization;
using System.Text.Json;

namespace GD.SaveEditor.Services.GameData.Items;

/// <summary>How the value(s) of a stat are read from a record and shown.</summary>
public enum ItemStatKind
{
    /// <summary>One number, e.g. <c>characterLife</c> → "+524 Health".</summary>
    Value,

    /// <summary>A min and an optional max field, e.g. <c>offensiveFireMin</c>/<c>Max</c> → "12-20 Fire Damage".</summary>
    DamageRange,

    /// <summary>Damage per second and a duration, e.g. <c>offensiveSlowFireMin</c> → "60 Burn Damage over 3 Seconds".</summary>
    DamageOverTime
}

/// <summary>One stat the tooltip knows how to show.</summary>
/// <param name="Field">Record field (the min field for damage).</param>
/// <param name="Format">Text with <c>{0}</c> (value or min), <c>{1}</c> (max) and <c>{2}</c> (duration).</param>
/// <param name="Scales">Whether the game rolls the stat around its record value (attribute scale / loot jitter).</param>
public sealed record ItemStatDefinition(
    string Field,
    string Format,
    ItemStatKind Kind = ItemStatKind.Value,
    string? MaxField = null,
    string? DurationField = null,
    bool Scales = true);

/// <summary>A stat line of a tooltip: "+54% Elemental Damage" with its roll range "[50-61]".</summary>
public sealed record ItemStatLine(string Text, string? Range);

/// <summary>
/// Adds up the stats of several records (base item, prefix, suffix, ...) like the game merges them into one list,
/// in the order of the game's tooltip.
/// </summary>
public sealed class ItemStatBlock
{
    private readonly Dictionary<ItemStatDefinition, Accumulator> _stats = [];

    /// <summary>
    /// Adds the stats of a record. <paramref name="scalePercent"/> is the width of the roll (e.g. 20 → ±10%) taken
    /// from <c>attributeScalePercent</c> (items) or <c>lootRandomizerJitter</c> (affixes).
    /// </summary>
    /// <param name="useLastValue">Fields with several values take the last (a completed component), else the first.</param>
    /// <param name="exclude">Fields shown elsewhere (the base armor of an item, ...).</param>
    public void Add(JsonElement fields, double scalePercent = 0, bool useLastValue = false, IReadOnlySet<string>? exclude = null)
    {
        if (fields.ValueKind != JsonValueKind.Object)
            return;

        var spread = Math.Clamp(scalePercent, 0, 200) / 200d;
        foreach (var definition in ItemStatDefinitions.All)
        {
            if (exclude?.Contains(definition.Field) == true)
                continue;

            var value = GetNumber(fields, definition.Field, useLastValue);
            if (value is not { } v || Math.Abs(v) < 0.0001)
                continue;

            var max = definition.MaxField is { } maxField ? GetNumber(fields, maxField, useLastValue) : null;
            var duration = definition.DurationField is { } durationField ? GetNumber(fields, durationField, useLastValue) : null;
            if (definition.Kind == ItemStatKind.DamageOverTime && duration is not > 0)
                continue;

            if (!_stats.TryGetValue(definition, out var accumulator))
                _stats[definition] = accumulator = new Accumulator();

            var s = definition.Scales ? spread : 0;
            switch (definition.Kind)
            {
                case ItemStatKind.DamageOverTime:
                    var total = v * duration!.Value;
                    accumulator.Add(total, total * (1 - s), total * (1 + s));
                    accumulator.Duration = Math.Max(accumulator.Duration, duration.Value);
                    break;
                case ItemStatKind.DamageRange:
                    accumulator.Add(v, v * (1 - s), v * (1 + s));
                    accumulator.MaxValue += max is { } m && m > v ? m : v;
                    break;
                default:
                    accumulator.Add(v, v * (1 - s), v * (1 + s));
                    break;
            }
        }
    }

    public bool IsEmpty => _stats.Count == 0;

    /// <summary>Whether any line has a roll range.</summary>
    public bool HasRanges => GetLines().Any(l => l.Range is not null);

    public IReadOnlyList<ItemStatLine> GetLines()
    {
        var lines = new List<ItemStatLine>();
        foreach (var definition in ItemStatDefinitions.All)
        {
            if (!_stats.TryGetValue(definition, out var stat))
                continue;

            string text;
            string? range = null;
            switch (definition.Kind)
            {
                case ItemStatKind.DamageRange:
                    text = Math.Round(stat.MaxValue) > Math.Round(stat.Value)
                        ? string.Format(CultureInfo.InvariantCulture, definition.Format, Number(stat.Value), Number(stat.MaxValue), string.Empty)
                        : ApplySign(string.Format(CultureInfo.InvariantCulture, StripMax(definition.Format), Number(stat.Value)), stat.Value);
                    break;
                case ItemStatKind.DamageOverTime:
                    text = string.Format(CultureInfo.InvariantCulture, definition.Format, Number(stat.Value), string.Empty, Number(stat.Duration));
                    break;
                default:
                    text = ApplySign(string.Format(CultureInfo.InvariantCulture, definition.Format, Number(stat.Value)), stat.Value);
                    if (Number(stat.Min) != Number(stat.Max))
                        range = $"[{Number(stat.Min)}-{Number(stat.Max)}]";
                    break;
            }

            lines.Add(new ItemStatLine(text, range));
        }

        return lines;
    }

    /// <summary>"+{0} Health" with a negative value reads "-10 Health", not "+-10 Health".</summary>
    private static string ApplySign(string text, double value) =>
        value < 0 && text.StartsWith("+-", StringComparison.Ordinal) ? text[1..] : text;

    /// <summary>"{0}-{1} Fire Damage" → "+{0} Fire Damage" when there is no max.</summary>
    private static string StripMax(string format) => "+" + format.Replace("{0}-{1}", "{0}", StringComparison.Ordinal);

    /// <summary>Whole numbers without decimals, others with one: "524", "2.5".</summary>
    public static string Number(double value)
    {
        var rounded = Math.Round(value, 1, MidpointRounding.AwayFromZero);
        return Math.Abs(rounded - Math.Round(rounded)) < 0.05
            ? Math.Round(rounded, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)
            : rounded.ToString("0.0", CultureInfo.InvariantCulture);
    }

    /// <summary>A number field; for a field with several values, the first (or last) one.</summary>
    public static double? GetNumber(JsonElement fields, string name, bool useLastValue = false)
    {
        if (fields.ValueKind != JsonValueKind.Object || !fields.TryGetProperty(name, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Array)
        {
            var values = value.EnumerateArray().ToList();
            if (values.Count == 0)
                return null;
            value = useLastValue ? values[^1] : values[0];
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var d) => d,
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            JsonValueKind.True => 1,
            _ => null
        };
    }

    /// <summary>A string field; for a field with several values, the first non-empty one.</summary>
    public static string? GetString(JsonElement fields, string name)
    {
        if (fields.ValueKind != JsonValueKind.Object || !fields.TryGetProperty(name, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Array)
            value = value.EnumerateArray().FirstOrDefault(v => v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 });

        return value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;
    }

    private sealed class Accumulator
    {
        public double Value;
        public double Min;
        public double Max;
        public double MaxValue;
        public double Duration;

        public void Add(double value, double min, double max)
        {
            Value += value;
            Min += Math.Min(min, max);
            Max += Math.Max(min, max);
        }
    }
}

/// <summary>The stats of item, affix, component and augment records, in the order of the game's tooltip.</summary>
public static class ItemStatDefinitions
{
    private static readonly (string Field, string Name)[] DamageTypes =
    [
        ("Physical", "Physical"), ("Pierce", "Pierce"), ("Elemental", "Elemental"), ("Fire", "Fire"), ("Cold", "Cold"),
        ("Lightning", "Lightning"), ("Poison", "Acid"), ("Life", "Vitality"), ("Aether", "Aether"), ("Chaos", "Chaos")
    ];

    private static readonly (string Field, string Name)[] DotTypes =
    [
        ("Bleeding", "Bleeding"), ("Physical", "Internal Trauma"), ("Fire", "Burn"), ("Cold", "Frostburn"),
        ("Lightning", "Electrocute"), ("Poison", "Poison"), ("Life", "Vitality Decay")
    ];

    private static readonly (string Field, string Name)[] Resistances =
    [
        ("Physical", "Physical"), ("Pierce", "Pierce"), ("Fire", "Fire"), ("Cold", "Cold"), ("Lightning", "Lightning"),
        ("Poison", "Poison & Acid"), ("Life", "Vitality"), ("Aether", "Aether"), ("Chaos", "Chaos"),
        ("ElementalResistance", "Elemental"), ("Bleeding", "Bleeding")
    ];

    public static IReadOnlyList<ItemStatDefinition> All { get; } = Build();

    private static List<ItemStatDefinition> Build()
    {
        var all = new List<ItemStatDefinition>
        {
            new("defensiveProtection", "+{0} Armor", Scales: false),
            new("defensiveBlock", "{0}% Chance to Block", Scales: false),
            new("blockValue", "+{0} Damage Blocked", Scales: false)
        };

        // Flat and over-time damage
        all.AddRange(DamageTypes.Select(t => new ItemStatDefinition(
            $"offensive{t.Field}Min", $"{{0}}-{{1}} {t.Name} Damage", ItemStatKind.DamageRange, $"offensive{t.Field}Max")));
        all.AddRange(DotTypes.Select(t => new ItemStatDefinition(
            $"offensiveSlow{t.Field}Min", $"{{0}} {t.Name} Damage over {{2}} Seconds", ItemStatKind.DamageOverTime,
            DurationField: $"offensiveSlow{t.Field}DurationMin")));

        // Damage modifiers
        all.Add(new("offensiveTotalDamageModifier", "+{0}% to All Damage"));
        all.AddRange(DamageTypes.Select(t => new ItemStatDefinition($"offensive{t.Field}Modifier", $"+{{0}}% {t.Name} Damage")));
        all.AddRange(DotTypes.Select(t => new ItemStatDefinition($"offensiveSlow{t.Field}Modifier", $"+{{0}}% {t.Name} Damage")));
        all.Add(new("offensiveCritDamageModifier", "+{0}% Crit Damage"));

        // Attributes and abilities
        all.AddRange(
        [
            new("characterStrength", "+{0} Physique"),
            new("characterDexterity", "+{0} Cunning"),
            new("characterIntelligence", "+{0} Spirit"),
            new("characterStrengthModifier", "+{0}% Physique"),
            new("characterDexterityModifier", "+{0}% Cunning"),
            new("characterIntelligenceModifier", "+{0}% Spirit"),
            new("characterLife", "+{0} Health"),
            new("characterLifeModifier", "+{0}% Health"),
            new("characterMana", "+{0} Energy"),
            new("characterManaModifier", "+{0}% Energy"),
            new("characterOffensiveAbility", "+{0} Offensive Ability"),
            new("characterOffensiveAbilityModifier", "+{0}% Offensive Ability"),
            new("characterDefensiveAbility", "+{0} Defensive Ability"),
            new("characterDefensiveAbilityModifier", "+{0}% Defensive Ability"),
            new("characterLifeRegen", "+{0} Health Regenerated per second"),
            new("characterLifeRegenModifier", "Increases Health Regeneration by {0}%"),
            new("characterManaRegen", "+{0} Energy Regenerated per second"),
            new("characterManaRegenModifier", "Increases Energy Regeneration by {0}%"),
            new("characterAttackSpeedModifier", "+{0}% Attack Speed"),
            new("characterSpellCastSpeedModifier", "+{0}% Casting Speed"),
            new("characterRunSpeedModifier", "+{0}% Movement Speed"),
            new("characterTotalSpeedModifier", "+{0}% Total Speed"),
            new("characterDodgePercent", "{0}% Chance to Avoid Melee Attacks"),
            new("characterDeflectProjectile", "{0}% Chance to Avoid Projectiles"),
            new("skillCooldownReduction", "-{0}% Skill Cooldown Reduction"),
            new("skillManaCostReduction", "-{0}% Skill Energy Cost"),
            new("characterIncreasedExperience", "+{0}% Experience Gained"),
            new("defensiveProtectionModifier", "Increases Armor by {0}%"),
            new("defensiveAbsorptionModifier", "Increases Armor Absorption by {0}%")
        ]);

        // Resistances
        all.AddRange(Resistances.Select(t => new ItemStatDefinition(
            t.Field == "ElementalResistance" ? "defensiveElementalResistance" : $"defensive{t.Field}",
            $"{{0}}% {t.Name} Resistance")));
        all.AddRange(Resistances.Where(t => t.Field != "ElementalResistance").Select(t => new ItemStatDefinition(
            $"defensive{t.Field}MaxResist", $"+{{0}}% Maximum {t.Name} Resistance")));
        all.AddRange(
        [
            new("defensiveStun", "{0}% Stun Resistance"),
            new("defensiveFreeze", "{0}% Freeze Resistance"),
            new("defensiveTrap", "{0}% Entrapment Resistance"),
            new("defensiveDisruption", "{0}% Skill Disruption Protection"),
            new("defensiveTotalSpeedResistance", "{0}% Slow Resistance"),
            new("defensiveReflect", "{0}% Reduction to Reflected Damage")
        ]);

        // Retaliation
        all.AddRange(DamageTypes.Where(t => t.Field != "Elemental").Select(t => new ItemStatDefinition(
            $"retaliation{t.Field}Min", $"{{0}}-{{1}} {t.Name} Retaliation", ItemStatKind.DamageRange, $"retaliation{t.Field}Max")));
        all.Add(new("retaliationTotalDamageModifier", "+{0}% to All Retaliation Damage"));

        return all;
    }
}
