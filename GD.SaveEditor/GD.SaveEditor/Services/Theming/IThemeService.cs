using System.Windows;

namespace GD.SaveEditor.Services.Theming;

/// <summary>Switches the application color palette at runtime.</summary>
public interface IThemeService
{
    /// <summary>Theme currently applied to the application resources.</summary>
    AppTheme CurrentTheme { get; }

    /// <summary>Replaces the active palette. Every <c>DynamicResource</c> brush updates immediately.</summary>
    void Apply(AppTheme theme);

    /// <summary>Loads a theme palette without applying it (e.g. for previews).</summary>
    ResourceDictionary GetPalette(AppTheme theme);
}
