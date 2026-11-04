using System.Windows;

namespace GD.SaveEditor.Services.Theming;

public sealed class ThemeService : IThemeService
{
    /// <summary>Folder (inside the assembly) that holds one palette dictionary per theme.</summary>
    private const string PaletteFolder = "Themes/Colors/";

    private readonly Dictionary<AppTheme, ResourceDictionary> _cache = new();

    public AppTheme CurrentTheme { get; private set; } = AppTheme.DefaultNight;

    public void Apply(AppTheme theme)
    {
        if (!Enum.IsDefined(theme))
            theme = AppTheme.DefaultNight;

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var palette = GetPalette(theme);

        var index = -1;
        for (var i = 0; i < dictionaries.Count; i++)
        {
            if (IsPalette(dictionaries[i]))
            {
                index = i;
                break;
            }
        }

        if (index >= 0)
        {
            if (!ReferenceEquals(dictionaries[index], palette))
                dictionaries[index] = palette;
        }
        else
        {
            // Palette must come before the style dictionaries that reference its brushes.
            dictionaries.Insert(0, palette);
        }

        CurrentTheme = theme;
    }

    public ResourceDictionary GetPalette(AppTheme theme)
    {
        if (!_cache.TryGetValue(theme, out var palette))
        {
            palette = new ResourceDictionary
            {
                Source = new Uri($"/GD.SaveEditor;component/{PaletteFolder}{theme}.xaml", UriKind.Relative)
            };
            _cache[theme] = palette;
        }

        return palette;
    }

    private static bool IsPalette(ResourceDictionary dictionary) =>
        dictionary.Source?.OriginalString.Contains(PaletteFolder, StringComparison.OrdinalIgnoreCase) == true;
}
