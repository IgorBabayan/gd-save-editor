using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using GD.SaveEditor.Presentation.ViewModels.Factions;
using Material.Icons;
using Material.Icons.Avalonia;

namespace GD.SaveEditor.Presentation.Views.Factions;

/// <summary>"Factions" tab of the selected character: the reputation with every faction.</summary>
public partial class FactionsView : UserControl
{
    public FactionsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Rank badge of a faction: a themed menu of the ranks from Nemesis to Revered (the current one checked); picking one
    /// sets the reputation to where that rank starts.
    /// </summary>
    private void OnTierButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: FactionItemViewModel faction } button || !faction.CanEdit)
            return;

        var menu = new ContextMenu
        {
            Placement = PlacementMode.Bottom,
            Theme = FindTheme("ThemedContextMenuStyle")
        };
        var itemTheme = FindTheme("ThemedMenuItemStyle");
        var separatorTheme = FindTheme("ThemedMenuSeparatorStyle");

        var current = faction.Tier;
        var hostileSection = true;
        foreach (var tier in faction.Tiers.Reverse())
        {
            // Reputation ranks first (Revered at the top), then a line, then the infamy ranks.
            if (tier.IsHostile && hostileSection)
            {
                menu.Items.Add(new Separator { Theme = separatorTheme });
                hostileSection = false;
            }

            var item = new MenuItem
            {
                Header = $"{tier.Name}  ({tier.Value:N0})",
                Theme = itemTheme,
                Icon = tier == current ? new MaterialIcon { Kind = MaterialIconKind.Check, Width = 16, Height = 16 } : null
            };
            item.Click += (_, _) => faction.SetTier(tier);
            menu.Items.Add(item);
        }

        menu.Open(button);
    }

    private ControlTheme? FindTheme(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) ? value as ControlTheme : null;
}
