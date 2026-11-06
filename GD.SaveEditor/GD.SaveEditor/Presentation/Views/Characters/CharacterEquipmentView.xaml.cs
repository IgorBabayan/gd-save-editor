using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using GD.SaveEditor.Presentation.ViewModels.Characters;
using GD.SaveEditor.Services.GameData.Items;
using Material.Icons;
using Material.Icons.WPF;

namespace GD.SaveEditor.Presentation.Views.Characters;

/// <summary>
/// "Character" tab: the equipment screen and attributes of the selected character. A click on a slot opens the item
/// inspector next to it: to change or take off its item, or to build a new item for an empty slot. A right-click on a
/// slot offers "Change…" and "Remove", or on an empty slot the kinds of item it takes.
/// </summary>
public partial class CharacterEquipmentView : UserControl
{
    public CharacterEquipmentView()
    {
        InitializeComponent();
    }

    private CharacterEquipmentViewModel? ViewModel => DataContext as CharacterEquipmentViewModel;

    private void OnSlotMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ContentControl { Content: EquipmentSlotViewModel slot } host || ViewModel is not { CanEditItems: true } viewModel)
            return;

        e.Handled = true;
        // After the mouse input is done, so the inspector takes the focus.
        Dispatcher.InvokeAsync(() => viewModel.EditSlot(slot, host), DispatcherPriority.Input);
    }

    private void OnSlotMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ContentControl { Content: EquipmentSlotViewModel { Slot: { } slotKind } slot } host
            || ViewModel is not { CanEditItems: true } viewModel)
            return;

        e.Handled = true;
        var menu = new ContextMenu
        {
            PlacementTarget = host,
            Placement = PlacementMode.MousePoint
        };
        if (TryFindResource("ThemedContextMenuStyle") is Style menuStyle)
            menu.Style = menuStyle;
        var itemStyle = TryFindResource("ThemedMenuItemStyle") as Style;

        if (!slot.IsEmpty)
        {
            var edit = new MenuItem { Header = "Change…", Icon = MenuIcon(MaterialIconKind.PencilOutline), Style = itemStyle };
            // After the menu has closed, so the inspector (or the question) takes the focus.
            edit.Click += (_, _) => Dispatcher.InvokeAsync(() => viewModel.EditSlot(slot, host), DispatcherPriority.Background);
            menu.Items.Add(edit);
            menu.Items.Add(new Separator { Style = TryFindResource("ThemedMenuSeparatorStyle") as Style });

            var remove = new MenuItem { Header = "Remove", Icon = MenuIcon(MaterialIconKind.DeleteOutline), Style = itemStyle };
            remove.Click += (_, _) => Dispatcher.InvokeAsync(() => viewModel.RemoveItem(slot), DispatcherPriority.Background);
            menu.Items.Add(remove);
        }
        else
        {
            // Title: a menu item that can be neither highlighted nor clicked.
            menu.Items.Add(new MenuItem
            {
                Header = new TextBlock
                {
                    Text = "Equip a new item",
                    FontWeight = FontWeights.SemiBold,
                    Style = TryFindResource("SecondaryTextStyle") as Style
                },
                Style = itemStyle,
                Focusable = false,
                IsHitTestVisible = false
            });
            menu.Items.Add(new Separator { Style = TryFindResource("ThemedMenuSeparatorStyle") as Style });

            foreach (var kind in EquipmentSlots.GetKinds(slotKind))
            {
                var entry = new MenuItem { Header = NewItemKinds.GetName(kind), Icon = MenuIcon(NewItemKinds.GetIconName(kind)), Style = itemStyle };
                var chosen = kind;
                entry.Click += (_, _) => Dispatcher.InvokeAsync(() => viewModel.EditSlot(slot, host, chosen), DispatcherPriority.Background);
                menu.Items.Add(entry);
            }
        }

        menu.IsOpen = true;
    }

    private static MaterialIcon MenuIcon(MaterialIconKind kind) => new() { Kind = kind, Width = 16, Height = 16 };

    /// <summary>A 16 px icon by its <see cref="MaterialIconKind"/> name; <c>null</c> (no icon) for an unknown name.</summary>
    private static MaterialIcon? MenuIcon(string kind) =>
        Enum.TryParse<MaterialIconKind>(kind, out var iconKind) ? MenuIcon(iconKind) : null;
}
