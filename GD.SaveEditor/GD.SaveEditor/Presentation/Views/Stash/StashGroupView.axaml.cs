using GD.SaveEditor.Presentation.Views.Inventory;

namespace GD.SaveEditor.Presentation.Views.Stash;

/// <summary>
/// One stash in the "Stash" tab (the personal stash or the shared stash): its tab buttons and the grid of the selected
/// tab. Moving, adding, changing and removing items is done by <see cref="ItemGridView"/>.
/// </summary>
public partial class StashGroupView : ItemGridView
{
    public StashGroupView()
    {
        InitializeComponent();
    }
}
