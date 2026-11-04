using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GD.SaveEditor.Presentation.Converters;

/// <summary><see cref="Visibility.Visible"/> when every bound value is <c>true</c>, otherwise <see cref="Visibility.Collapsed"/>.</summary>
public sealed class AllTrueToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.All(v => v is true) ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
