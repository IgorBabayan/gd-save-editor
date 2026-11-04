using System.IO;
using Microsoft.Win32;

namespace GD.SaveEditor.Presentation.Services;

public sealed class FolderPickerService : IFolderPickerService
{
    public string? PickFolder(string title, string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            dialog.InitialDirectory = initialDirectory;

        var owner = WindowHelper.GetActiveWindow();
        var result = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);

        return result == true ? dialog.FolderName : null;
    }
}
