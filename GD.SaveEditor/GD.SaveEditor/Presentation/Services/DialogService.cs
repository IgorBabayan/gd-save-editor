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
        window.Owner = WindowHelper.GetActiveWindow();
        return window.ShowDialog() == true;
    }

    public bool Confirm(string title, string message)
    {
        var window = new ConfirmationWindow(title, message)
        {
            Owner = WindowHelper.GetActiveWindow()
        };
        return window.ShowDialog() == true;
    }

    public DatabaseGenerationResult? ShowDatabaseGenerator()
    {
        var window = services.GetRequiredService<DatabaseGeneratorWindow>();
        window.Owner = WindowHelper.GetActiveWindow();
        window.ShowDialog();
        return window.Result;
    }

    public ItemBuilderResult ShowItemBuilder(SaveItem? current)
    {
        var viewModel = new ItemBuilderViewModel(
            services.GetRequiredService<IItemCatalog>(),
            services.GetRequiredService<IGameItemProvider>(),
            services.GetRequiredService<IGameIconProvider>(),
            Confirm,
            current);

        var removed = false;
        viewModel.RemoveRequested += (_, _) => removed = true;

        var window = new ItemBuilderWindow(viewModel)
        {
            Owner = WindowHelper.GetActiveWindow()
        };
        window.ShowDialog();
        return new ItemBuilderResult(removed ? null : window.Result, removed);
    }
}
