using GD.SaveEditor.Presentation.ViewModels.Inventory;
using GD.SaveEditor.Presentation.ViewModels.Waypoints;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Presentation.ViewModels.Characters;

/// <summary>
/// Details of the character selected in the character list, split into the
/// "Inventory" and "Waypoints" tabs of the main window.
/// </summary>
public sealed partial class CharacterViewModel : ViewModelBase
{
    public CharacterViewModel(CharacterSave character)
    {
        Character = character;
        Inventory = new InventoryViewModel(character);
        Waypoints = new WaypointsViewModel(character);
        RefreshHasUnsavedChanges();
    }

    public CharacterSave Character { get; }

    /// <summary>"Inventory" tab.</summary>
    public InventoryViewModel Inventory { get; }

    /// <summary>"Waypoints" tab.</summary>
    public WaypointsViewModel Waypoints { get; }

    /// <summary>Something was changed that is not written to the save file yet.</summary>
    [ObservableProperty]
    private bool _hasUnsavedChanges;

    /// <summary>
    /// The bytes to write into <c>player.gdc</c>. The tabs cannot change anything yet, so this is the file as read;
    /// editors will build the changed file here.
    /// </summary>
    public byte[] GetSaveData() => Character.RawData;

    /// <summary>Call when a tab changed something, so the Save button follows the actual difference to the file.</summary>
    public void RefreshHasUnsavedChanges() =>
        HasUnsavedChanges = Inventory.HasChanges || Waypoints.HasChanges;
}
