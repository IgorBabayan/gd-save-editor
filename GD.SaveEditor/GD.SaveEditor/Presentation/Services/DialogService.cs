using Avalonia.Controls;
using GD.SaveEditor.Presentation.ViewModels.Inventory;
using GD.SaveEditor.Presentation.Views.Database;
using GD.SaveEditor.Presentation.Views.Dialogs;
using GD.SaveEditor.Presentation.Views.Inventory;
using GD.SaveEditor.Presentation.Views.Settings;
using GD.SaveEditor.Services.GameData;
using GD.SaveEditor.Services.GameData.Database;
using GD.SaveEditor.Services.GameData.Items;
using GD.SaveEditor.Services.GameData.Textures;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Presentation.Services;

public sealed class DialogService(IServiceProvider services) : IDialogService
{
    public bool ShowSettings()
    {
        var window = services.GetRequiredService<SettingsWindow>();
        return WindowHelper.ShowModal<bool>(window);
    }

    public bool Confirm(string title, string message)
    {
        var window = new ConfirmationWindow(title, message);
        return WindowHelper.ShowModal<bool>(window);
    }

    public DatabaseGenerationResult? ShowDatabaseGenerator()
    {
        var window = services.GetRequiredService<DatabaseGeneratorWindow>();
        WindowHelper.ShowModal<object>(window);
        return window.Result;
    }

    public ItemBuilderResult ShowItemBuilder(SaveItem? current)
    {
        var viewModel = CreateItemBuilder(current);
        var removed = false;
        viewModel.RemoveRequested += (_, _) => removed = true;

        var built = ShowItemBuilderWindow(viewModel);
        return new ItemBuilderResult(removed ? null : built, removed);
    }

    public void ShowItemInspector(object? anchor, SaveItem? current, Action<BuiltItem> applied, Action? removed = null, NewItemKind? kind = null)
    {
        var viewModel = CreateItemBuilder(current, kind);
        if (removed is not null)
            viewModel.RemoveRequested += (_, _) => removed();

        // Next to the item; the expand button swaps it for the full window over the same view model.
        if (anchor is Control element
            && ItemInspectorAdorner.TryShow(element, viewModel, applied, vm => ShowItemBuilderWindow(vm)))
            return;

        if (ShowItemBuilderWindow(viewModel) is { } built)
            applied(built);
    }

    /// <summary>Closes the item inspector without applying it, if one is open.</summary>
    public void CloseItemInspector() => ItemInspectorAdorner.CloseCurrent();

    private ItemBuilderViewModel CreateItemBuilder(SaveItem? current, NewItemKind? kind = null) =>
        new(services.GetRequiredService<IItemCatalog>(),
            services.GetRequiredService<IGameItemProvider>(),
            services.GetRequiredService<IGameIconProvider>(),
            Confirm,
            current,
            kind);

    /// <summary>The full builder window (modal) over <paramref name="viewModel"/>; the built item, or <c>null</c>.</summary>
    private static BuiltItem? ShowItemBuilderWindow(ItemBuilderViewModel viewModel)
    {
        var window = new ItemBuilderWindow(viewModel);
        WindowHelper.ShowModal<object>(window);
        return window.Result;
    }
}
