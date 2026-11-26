using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace GD.SaveEditor.Services.Theming;

public sealed class ThemeService : IThemeService
{
    /// <summary>Folder (inside the assembly) that holds one palette dictionary per theme.</summary>
    private const string PaletteFolder = "avares://GD.SaveEditor/Themes/Colors/";

    private readonly Dictionary<AppTheme, IResourceDictionary> _cache = new();

    /// <summary>The palette currently merged into the application resources.</summary>
    private IResourceProvider? _applied;

    public AppTheme CurrentTheme { get; private set; } = AppTheme.DefaultNight;

    public void Apply(AppTheme theme)
    {
        if (!Enum.IsDefined(theme))
            theme = AppTheme.DefaultNight;

        if (Application.Current is not { } app)
            return;

        var dictionaries = app.Resources.MergedDictionaries;
        var palette = GetPalette(theme);

        // The palette is the first merged dictionary (App.axaml); it must come before the dictionaries using its brushes.
        var index = _applied is not null ? dictionaries.IndexOf(_applied) : 0;
        if (index < 0)
            index = 0;

        if (dictionaries.Count > index && !ReferenceEquals(dictionaries[index], palette))
            dictionaries[index] = palette;
        else if (dictionaries.Count == 0)
            dictionaries.Add(palette);

        _applied = palette;
        // Fluent parts (drop-downs, tooltips, the text caret) follow the palette's brightness.
        app.RequestedThemeVariant = theme == AppTheme.DefaultLight ? ThemeVariant.Light : ThemeVariant.Dark;
        CurrentTheme = theme;
    }

    public IResourceDictionary GetPalette(AppTheme theme)
    {
        if (!_cache.TryGetValue(theme, out var palette))
        {
            palette = (IResourceDictionary)AvaloniaXamlLoader.Load(new Uri($"{PaletteFolder}{theme}.axaml"));
            _cache[theme] = palette;
        }

        return palette;
    }
}
