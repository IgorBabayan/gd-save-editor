using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using GD.SaveEditor.Services.GameData.Database;

namespace GD.SaveEditor.Presentation.Converters;

/// <summary>
/// The game's color of an <see cref="ItemRarity"/>. Without a parameter: a solid brush for frames and names.
/// With <c>Glow</c>: a radial glow for the slot background, like the colored slots of the equipment screen.
/// With <c>Tint</c>: a faint flat tint, for the header of the item inspector.
/// The brushes are immutable and shared by every item, so a full bag costs no brush allocations.
/// </summary>
public sealed class ItemRarityToBrushConverter : IValueConverter
{
    private static readonly Dictionary<ItemRarity, (IBrush Solid, IBrush Glow, IBrush Tint)> Brushes = new()
    {
        [ItemRarity.Unknown] = Create(Color.FromRgb(0x8A, 0x8A, 0x8A)),
        [ItemRarity.Common] = Create(Color.FromRgb(0xD8, 0xD8, 0xD8)),
        [ItemRarity.Magical] = Create(Color.FromRgb(0xF2, 0xE1, 0x4B)),
        [ItemRarity.Rare] = Create(Color.FromRgb(0x4C, 0xE0, 0x3C)),
        [ItemRarity.Epic] = Create(Color.FromRgb(0x3C, 0x9C, 0xFF)),
        [ItemRarity.Legendary] = Create(Color.FromRgb(0xA8, 0x5C, 0xFF)),
        [ItemRarity.Relic] = Create(Color.FromRgb(0xE8, 0x8A, 0x2C)),
        [ItemRarity.Component] = Create(Color.FromRgb(0xE4, 0xC0, 0x7A)),
        [ItemRarity.Quest] = Create(Color.FromRgb(0xC9, 0x6B, 0xE0))
    };

    /// <summary>The solid brush of <paramref name="rarity"/> (for code).</summary>
    public static IBrush GetSolid(ItemRarity rarity) =>
        Brushes.TryGetValue(rarity, out var brushes) ? brushes.Solid : Brushes[ItemRarity.Unknown].Solid;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // A binding that cannot resolve (e.g. no selection) falls back to the target's FallbackValue.
        if (value is not ItemRarity rarity)
            return value is null ? AvaloniaProperty.UnsetValue : Brushes[ItemRarity.Unknown].Solid;

        var brushes = Brushes.TryGetValue(rarity, out var found) ? found : Brushes[ItemRarity.Unknown];
        return (parameter as string)?.ToLowerInvariant() switch
        {
            "glow" => brushes.Glow,
            "tint" => brushes.Tint,
            _ => brushes.Solid
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        BindingOperations.DoNothing;

    private static (IBrush Solid, IBrush Glow, IBrush Tint) Create(Color color)
    {
        var solid = new ImmutableSolidColorBrush(color);

        var glow = new ImmutableRadialGradientBrush(
            [
                new ImmutableGradientStop(0, Color.FromArgb(0x70, color.R, color.G, color.B)),
                new ImmutableGradientStop(0.55, Color.FromArgb(0x26, color.R, color.G, color.B)),
                new ImmutableGradientStop(1, Color.FromArgb(0x00, color.R, color.G, color.B))
            ],
            center: new RelativePoint(0.5, 0.55, RelativeUnit.Relative),
            gradientOrigin: new RelativePoint(0.5, 0.55, RelativeUnit.Relative),
            radiusX: new RelativeScalar(0.75, RelativeUnit.Relative),
            radiusY: new RelativeScalar(0.75, RelativeUnit.Relative));

        var tint = new ImmutableSolidColorBrush(Color.FromArgb(0x22, color.R, color.G, color.B));
        return (solid, glow, tint);
    }
}
