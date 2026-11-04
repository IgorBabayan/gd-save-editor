using GD.SaveEditor.Presentation.Views.Database;
using GD.SaveEditor.Presentation.Views.Dialogs;
using GD.SaveEditor.Presentation.Views.Settings;
using GD.SaveEditor.Services.GameData;

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
}
