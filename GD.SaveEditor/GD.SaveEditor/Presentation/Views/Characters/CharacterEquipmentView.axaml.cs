using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GD.SaveEditor.Presentation.ViewModels.Characters;
using GD.SaveEditor.Services.GameData.Items;
using Material.Icons;
using Material.Icons.Avalonia;

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
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Bubble);
    }

    private CharacterEquipmentViewModel? ViewModel => DataContext as CharacterEquipmentViewModel;

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (FindSlot(e.Source as Visual) is not { Content: EquipmentSlotViewModel slot } host
            || ViewModel is not { CanEditItems: true } viewModel)
            return;

        // Only a release over the slot that was pressed counts as a click.
        if (!host.IsPointerOver)
            return;

        e.Handled = true;
        if (e.InitialPressMouseButton == MouseButton.Right)
            OpenSlotMenu(slot, host, viewModel);
        else if (e.InitialPressMouseButton == MouseButton.Left)
            // After the pointer input is done, so the inspector takes the focus.
            Dispatcher.UIThread.Post(() => viewModel.EditSlot(slot, host), DispatcherPriority.Input);
    }

    private void OpenSlotMenu(EquipmentSlotViewModel slot, ContentControl host, CharacterEquipmentViewModel viewModel)
    {
        if (slot.Slot is not { } slotKind)
            return;

        var menu = new ContextMenu
        {
            Placement = PlacementMode.Pointer,
            Theme = FindTheme("ThemedContextMenuStyle")
        };
        var itemTheme = FindTheme("ThemedMenuItemStyle");

        if (!slot.IsEmpty)
        {
            var edit = new MenuItem { Header = "Change…", Icon = MenuIcon(MaterialIconKind.PencilOutline), Theme = itemTheme };
            // After the menu has closed, so the inspector (or the question) takes the focus.
            edit.Click += (_, _) => Dispatcher.UIThread.Post(() => viewModel.EditSlot(slot, host), DispatcherPriority.Background);
            menu.Items.Add(edit);
            menu.Items.Add(new Separator { Theme = FindTheme("ThemedMenuSeparatorStyle") });

            var remove = new MenuItem { Header = "Remove", Icon = MenuIcon(MaterialIconKind.DeleteOutline), Theme = itemTheme };
            remove.Click += (_, _) => Dispatcher.UIThread.Post(() => viewModel.RemoveItem(slot), DispatcherPriority.Background);
            menu.Items.Add(remove);
        }
        else
        {
            // Title: a menu item that can be neither highlighted nor clicked.
            var title = new MenuItem
            {
                Header = new TextBlock
                {
                    Text = "Equip a new item",
                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                    Theme = FindTheme("SecondaryTextStyle")
                },
                Theme = itemTheme
            };
            title.Classes.Add("title");
            menu.Items.Add(title);
            menu.Items.Add(new Separator { Theme = FindTheme("ThemedMenuSeparatorStyle") });

            foreach (var kind in EquipmentSlots.GetKinds(slotKind))
            {
                var entry = new MenuItem { Header = NewItemKinds.GetName(kind), Icon = MenuIcon(NewItemKinds.GetIconName(kind)), Theme = itemTheme };
                var chosen = kind;
                entry.Click += (_, _) => Dispatcher.UIThread.Post(() => viewModel.EditSlot(slot, host, chosen), DispatcherPriority.Background);
                menu.Items.Add(entry);
            }
        }

        menu.Open(host);
    }

    /// <summary>The slot (<c>slot</c> class) <paramref name="node"/> belongs to.</summary>
    private ContentControl? FindSlot(Visual? node)
    {
        for (; node is not null && !ReferenceEquals(node, this); node = node.GetVisualParent())
        {
            if (node is ContentControl control && control.Classes.Contains("slot"))
                return control;
        }

        return null;
    }

    private ControlTheme? FindTheme(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) ? value as ControlTheme : null;

    private static MaterialIcon MenuIcon(MaterialIconKind kind) => new() { Kind = kind, Width = 16, Height = 16 };

    /// <summary>A 16 px icon by its <see cref="MaterialIconKind"/> name; <c>null</c> (no icon) for an unknown name.</summary>
    private static MaterialIcon? MenuIcon(string kind) =>
        Enum.TryParse<MaterialIconKind>(kind, out var iconKind) ? MenuIcon(iconKind) : null;
}
