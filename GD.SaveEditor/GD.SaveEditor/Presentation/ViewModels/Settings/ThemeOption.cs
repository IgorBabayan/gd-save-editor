using System.Windows;
using System.Windows.Media;
using GD.SaveEditor.Services.Theming;

namespace GD.SaveEditor.Presentation.ViewModels.Settings;

/// <summary>One selectable theme in the Settings dialog, with brushes for its preview thumbnail.</summary>
public sealed class ThemeOption
{
    public ThemeOption(AppTheme theme, string displayName, ResourceDictionary palette)
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

    public Brush WindowBackground { get; }
    public Brush TitleBarBackground { get; }
    public Brush Border { get; }
    public Brush Foreground { get; }
    public Brush Accent { get; }

    /// <summary>Used by screen readers (ListBoxItem automation name).</summary>
    public override string ToString() => DisplayName;

    private static Brush GetBrush(ResourceDictionary palette, string key) =>
        palette[key] as Brush ?? Brushes.Transparent;
}
