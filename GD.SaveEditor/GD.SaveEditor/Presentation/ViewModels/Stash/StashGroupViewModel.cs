using GD.SaveEditor.Presentation.Services;
using GD.SaveEditor.Presentation.ViewModels.Inventory;
using GD.SaveEditor.Services.GameData.Database;
using GD.SaveEditor.Services.GameData.Textures;
using GD.SaveEditor.Services.Saves;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Presentation.ViewModels.Stash;

/// <summary>
/// A stash shown in the "Stash" tab: its tabs behind tab buttons and the grid of the selected one, like the game's
/// stash window. Items are moved, added, changed and removed as in the bags (see <see cref="ItemGridViewModel"/>).
/// </summary>
public abstract partial class StashGroupViewModel : ItemGridViewModel
{
    private IReadOnlyList<InventoryBagViewModel> _tabs = [];

    protected StashGroupViewModel(IGameItemProvider items, IGameIconProvider icons, IDialogService dialogs)
        : base(items, icons, dialogs)
    {
    }

    /// <summary>"Personal stash", "Shared stash (softcore)".</summary>
    public abstract string Title { get; }

    /// <summary>The tabs, bought ones first; tabs that were not bought are locked.</summary>
    public IReadOnlyList<InventoryBagViewModel> Tabs => _tabs;

    public override IReadOnlyList<InventoryBagViewModel> Containers => _tabs;

    public bool HasTabs => _tabs.Count > 0;

    /// <summary>Tab shown under the tab buttons.</summary>
    [ObservableProperty]
    private InventoryBagViewModel? _selectedTab;

    /// <summary>Why the stash is not shown or what is special about it; <c>null</c> when there is nothing to say.</summary>
    public virtual string? Notice => null;

    public bool HasNotice => Notice is not null;

    protected override InventoryBagViewModel? DefaultContainer =>
        SelectedTab is { IsAvailable: true } selected ? selected : _tabs.FirstOrDefault(t => t.IsAvailable);

    protected override string ContainerNoun => "stash tabs";

    /// <summary>The contents of every bought tab to write; <c>null</c> when the tabs are as in the file.</summary>
    public IReadOnlyList<IReadOnlyList<PlacedItem>>? ChangedTabs => ChangedContainers;

    public override void SelectContainer(InventoryBagViewModel container)
    {
        if (container.IsAvailable && _tabs.Contains(container))
            SelectedTab = container;
    }

    /// <summary>
    /// Creates the tab view models: one per tab of <paramref name="tabs"/>, then locked ones up to
    /// <paramref name="shownTabs"/>, then reads their names and icons.
    /// </summary>
    protected void SetTabs(IReadOnlyList<StashTab> tabs, int shownTabs, string namePrefix)
    {
        var width = tabs.FirstOrDefault()?.Width ?? CharacterStash.DefaultWidth;
        var height = tabs.FirstOrDefault()?.Height ?? CharacterStash.DefaultHeight;
        var list = new List<InventoryBagViewModel>(Math.Max(tabs.Count, shownTabs));
        for (var i = 0; i < Math.Max(tabs.Count, shownTabs); i++)
        {
            var number = (i + 1).ToString();
            if (i < tabs.Count)
            {
                var tab = tabs[i];
                // The game stores the cell as floats; they hold whole numbers.
                var items = tab.Items
                    .Select(item => new InventoryItem(item.Item, (int)MathF.Round(item.X), (int)MathF.Round(item.Y)))
                    .ToList();
                list.Add(new InventoryBagViewModel(i, $"{namePrefix} {number}", tab.Width, tab.Height, items, number));
            }
            else
            {
                list.Add(new InventoryBagViewModel(i, $"{namePrefix} {number}", width, height, [], number, isAvailable: false));
            }
        }

        _tabs = list;
        SelectedTab = list.FirstOrDefault(t => t.IsAvailable);
        _ = LoadItemsAsync();
    }
}

/// <summary>
/// Personal stash of the character (block 4 of <c>player.gdc</c>): tabs 1 to 8. A tab the character did not buy in the
/// game is not in the save file; it is shown locked, so nothing can be put there.
/// </summary>
public sealed class PersonalStashViewModel : StashGroupViewModel
{
    public PersonalStashViewModel(CharacterSave character, IGameItemProvider items, IGameIconProvider icons, IDialogService dialogs)
        : base(items, icons, dialogs)
    {
        Character = character;
        SetTabs(character.Stash?.Tabs ?? [], character.Stash is null ? 0 : CharacterStash.MaxTabs, "Tab");
    }

    public CharacterSave Character { get; }

    public override string Title => "Personal stash";

    public override bool HasItemData => Character.Stash is not null;

    public override bool CanEditItems => Character.Stash is { Tabs.Count: > 0 } && Character.CanRewrite;

    protected override string? ReadOnlyReason => Character.CanRewrite
        ? null
        : $"Items cannot be moved, added or changed for this character: {Character.RewriteError}";

    public override string? Notice =>
        Character.Stash is null ? "The personal stash could not be read from the save file."
        : Character.Stash.Tabs.Count == 0 ? "The character has not bought a stash tab yet."
        : null;
}

/// <summary>
/// The shared stash of every softcore (or every hardcore) character, read from <c>transfer.gst</c> /
/// <c>transfer.gsh</c>. It is written to its own file when the character is saved.
/// </summary>
public sealed class TransferStashViewModel : StashGroupViewModel
{
    public TransferStashViewModel(TransferStashLoad load, IGameItemProvider items, IGameIconProvider icons, IDialogService dialogs)
        : base(items, icons, dialogs)
    {
        Load = load;
        SetTabs(load.Stash?.Sacks ?? [], 0, "Tab");
    }

    public TransferStashLoad Load { get; }

    public TransferStash? Stash => Load.Stash;

    public bool Hardcore => Load.Hardcore;

    public override string Title => Load.Hardcore ? "Shared stash (hardcore)" : "Shared stash (softcore)";

    public override bool HasItemData => Load.Stash is not null;

    public override bool CanEditItems => Load.Stash is { Sacks.Count: > 0 };

    protected override string? ReadOnlyReason => null;

    public override string? Notice =>
        Load.Stash is null ? $"The shared stash could not be read: {Load.Error}"
        : Load.Stash.Sacks.Count == 0 ? "No shared stash tab was bought in the game yet."
        : $"Shared by every {(Load.Hardcore ? "hardcore" : "softcore")} character. Changes are written to {System.IO.Path.GetFileName(Load.Stash.FilePath)} on Save.";
}
