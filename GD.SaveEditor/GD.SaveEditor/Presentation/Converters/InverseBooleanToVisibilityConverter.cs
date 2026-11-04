using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GD.SaveEditor.Presentation.Converters;

/// <summary><c>true</c> → <see cref="Visibility.Collapsed"/>, anything else → <see cref="Visibility.Visible"/>.</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not Visibility.Visible;
}
