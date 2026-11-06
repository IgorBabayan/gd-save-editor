using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using GD.SaveEditor.Presentation.ViewModels.Factions;
using Material.Icons;
using Material.Icons.WPF;

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
    private void OnTierButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FactionItemViewModel faction } button || !faction.CanEdit)
            return;

        var menu = new ContextMenu
        {
            PlacementTarget = button,
            Placement = PlacementMode.Bottom
        };
        if (TryFindResource("ThemedContextMenuStyle") is Style menuStyle)
            menu.Style = menuStyle;
        var itemStyle = TryFindResource("ThemedMenuItemStyle") as Style;
        var separatorStyle = TryFindResource("ThemedMenuSeparatorStyle") as Style;

        var current = faction.Tier;
        var hostileSection = true;
        foreach (var tier in faction.Tiers.Reverse())
        {
            // Reputation ranks first (Revered at the top), then a line, then the infamy ranks.
            if (tier.IsHostile && hostileSection)
            {
                menu.Items.Add(new Separator { Style = separatorStyle });
                hostileSection = false;
            }

            var item = new MenuItem
            {
                Header = $"{tier.Name}  ({tier.Value:N0})",
                Style = itemStyle,
                Icon = tier == current ? new MaterialIcon { Kind = MaterialIconKind.Check, Width = 16, Height = 16 } : null
            };
            item.Click += (_, _) => faction.SetTier(tier);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }
}
