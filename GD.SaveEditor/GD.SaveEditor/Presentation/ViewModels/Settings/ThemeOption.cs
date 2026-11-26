using Avalonia.Controls;
using Avalonia.Media;
using GD.SaveEditor.Services.Theming;

namespace GD.SaveEditor.Presentation.ViewModels.Settings;

/// <summary>One selectable theme in the Settings dialog, with brushes for its preview thumbnail.</summary>
public sealed class ThemeOption
{
    public ThemeOption(AppTheme theme, string displayName, IResourceDictionary palette)
    {
        Theme = theme;
        DisplayName = displayName;
        WindowBackground = GetBrush(palette, "WindowBackgroundBrush");
        TitleBarBackground = GetBrush(palette, "TitleBarBackgroundBrush");
        Border = GetBrush(palette, "WindowBorderBrush");
        Foreground = GetBrush(palette, "WindowForegroundBrush");
        Accent = GetBrush(palette, "AccentBrush");
    }

    public AppTheme Theme { get; }
    public string DisplayName { get; }

    public IBrush WindowBackground { get; }
    public IBrush TitleBarBackground { get; }
    public IBrush Border { get; }
    public IBrush Foreground { get; }
    public IBrush Accent { get; }

    /// <summary>Used by screen readers (ListBoxItem automation name).</summary>
    public override string ToString() => DisplayName;

    private static IBrush GetBrush(IResourceDictionary palette, string key) =>
        palette.TryGetResource(key, null, out var value) && value is IBrush brush ? brush : Brushes.Transparent;
}
