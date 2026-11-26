using System.IO;
using Avalonia.Platform.Storage;

namespace GD.SaveEditor.Presentation.Services;

/// <summary>The platform's folder dialog (Windows, the XDG portal / GTK on Linux, NSOpenPanel on macOS).</summary>
public sealed class FolderPickerService : IFolderPickerService
{
    public string? PickFolder(string title, string? initialDirectory = null)
    {
        if (WindowHelper.GetActiveWindow()?.StorageProvider is not { CanPickFolder: true } storage)
            return null;

        return WindowHelper.Wait(PickAsync(storage, title, initialDirectory));
    }

    private static async Task<string?> PickAsync(IStorageProvider storage, string title, string? initialDirectory)
    {
        var options = new FolderPickerOpenOptions { Title = title, AllowMultiple = false };
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            options.SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(initialDirectory);

        var folders = await storage.OpenFolderPickerAsync(options);
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
