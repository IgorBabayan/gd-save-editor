using Avalonia.Data.Converters;

namespace GD.SaveEditor.Presentation.Converters;

/// <summary>Small stateless converters used from XAML with <c>{x:Static}</c>.</summary>
public static class AppConverters
{
    /// <summary><c>true</c> for a count above zero (e.g. hides an empty list).</summary>
    public static readonly IValueConverter IsPositive = new FuncValueConverter<int, bool>(count => count > 0);

    /// <summary><c>true</c> for zero (e.g. fades a material with nothing stored).</summary>
    public static readonly IValueConverter IsZero = new FuncValueConverter<int, bool>(value => value == 0);
}
