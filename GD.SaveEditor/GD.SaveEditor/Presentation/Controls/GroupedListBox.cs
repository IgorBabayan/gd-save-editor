using Avalonia.Controls;
using GD.SaveEditor.Presentation.ViewModels.Inventory;

namespace GD.SaveEditor.Presentation.Controls;

/// <summary>
/// A list whose rows are grouped by <see cref="ListGroupHeader"/> rows (see <see cref="ListGroupHeader.Group{T}"/>): the
/// header rows get the <c>groupHeader</c> class and can be neither selected nor focused, so a click or the arrow keys
/// only ever land on items. The rows stay virtualized like in any list box.
/// </summary>
public class GroupedListBox : ListBox
{
    protected override Type StyleKeyOverride => typeof(ListBox);

    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);

        // Containers are recycled between headers and items: set both states every time.
        var isHeader = item is ListGroupHeader;
        container.Classes.Set("groupHeader", isHeader);
        container.IsEnabled = !isHeader;
    }
}
