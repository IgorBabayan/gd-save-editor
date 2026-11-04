namespace GD.SaveEditor.Presentation.Services;

public interface IFolderPickerService
{
    /// <summary>Shows the system "Select folder" dialog.</summary>
    /// <returns>Selected folder path, or <c>null</c> if the user cancelled.</returns>
    string? PickFolder(string title, string? initialDirectory = null);
}
