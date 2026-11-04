namespace GD.SaveEditor.Services.Saves;

public interface ISaveFolderScanner
{
    /// <summary>
    /// Paths of the character files in <paramref name="saveFolder"/>: <c>_Name\player.gdc</c> in each sub folder.
    /// </summary>
    IReadOnlyList<string> FindCharacterFiles(string saveFolder);
}
