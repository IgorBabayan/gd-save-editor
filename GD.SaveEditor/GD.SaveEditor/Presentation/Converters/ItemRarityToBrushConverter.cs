using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using GD.SaveEditor.Services.GameData.Database;

namespace GD.SaveEditor.Presentation.Converters;

/// <summary>
/// The game's color of an <see cref="ItemRarity"/>. Without a parameter: a solid brush for frames and names.
/// With <c>Glow</c>: a radial glow for the slot background, like the colored slots of the equipment screen.
/// With <c>Tint</c>: a faint flat tint, for the header of the item inspector.
/// </summary>
public sealed class ItemRarityToBrushConverter : IValueConverter
{
    private static readonly Dictionary<ItemRarity, (Brush Solid, Brush Glow, Brush Tint)> Brushes = new()
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

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var brushes = Brushes[value is ItemRarity rarity && Brushes.ContainsKey(rarity) ? rarity : ItemRarity.Unknown];
        return (parameter as string)?.ToLowerInvariant() switch
        {
            "glow" => brushes.Glow,
            "tint" => brushes.Tint,
            _ => brushes.Solid
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        DependencyProperty.UnsetValue;

    private static (Brush Solid, Brush Glow, Brush Tint) Create(Color color)
    {
        var solid = new SolidColorBrush(color);
        solid.Freeze();

        var glow = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.55),
            Center = new Point(0.5, 0.55),
            RadiusX = 0.75,
            RadiusY = 0.75,
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x70, color.R, color.G, color.B), 0),
                new GradientStop(Color.FromArgb(0x26, color.R, color.G, color.B), 0.55),
                new GradientStop(Color.FromArgb(0x00, color.R, color.G, color.B), 1)
            }
        };
        glow.Freeze();

        var tint = new SolidColorBrush(Color.FromArgb(0x22, color.R, color.G, color.B));
        tint.Freeze();
        return (solid, glow, tint);
    }
}
