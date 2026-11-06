using GD.SaveEditor.Presentation.Services;
using GD.SaveEditor.Presentation.ViewModels.Inventory;
using GD.SaveEditor.Presentation.ViewModels.Waypoints;
using GD.SaveEditor.Services.GameData.Database;
using GD.SaveEditor.Services.GameData.Textures;
using GD.SaveEditor.Services.GameData.Waypoints;
using GD.SaveEditor.Services.Saves;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Presentation.ViewModels.Characters;

/// <summary>
/// Details of the character selected in the character list, split into the "Character" (equipment, attributes and
/// the bags under them) and "Waypoints" tabs of the main window.
/// </summary>
public sealed partial class CharacterViewModel : ViewModelBase
{
    private readonly ICharacterSaveEditor _saveEditor;

    public CharacterViewModel(
        CharacterSave character,
        IGameItemProvider items,
        IGameIconProvider icons,
        ICharacterSaveEditor saveEditor,
        IDialogService dialogs,
        IGameWaypointProvider waypoints)
    {
        _saveEditor = saveEditor;
        Character = character;
        Profile = new CharacterProfileViewModel(character);
        Equipment = new CharacterEquipmentViewModel(character, Profile, items, icons, dialogs);
        Inventory = new InventoryViewModel(character, items, icons, dialogs);
        Waypoints = new WaypointsViewModel(character, waypoints);

        Inventory.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(InventoryViewModel.HasChanges))
                RefreshHasUnsavedChanges();
        };
        Equipment.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CharacterEquipmentViewModel.HasChanges))
                RefreshHasUnsavedChanges();
        };
        Profile.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CharacterProfileViewModel.HasChanges))
                RefreshHasUnsavedChanges();
        };
        Waypoints.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WaypointsViewModel.HasChanges))
                RefreshHasUnsavedChanges();
        };

        RefreshHasUnsavedChanges();
    }

    public CharacterSave Character { get; }

    /// <summary>"Character" tab: equipment (which can be changed) and attributes.</summary>
    public CharacterEquipmentViewModel Equipment { get; }

    /// <summary>Name, sex, difficulty, tributes, attributes and points, beside the equipment in the "Character" tab.</summary>
    public CharacterProfileViewModel Profile { get; }

    /// <summary>Bags and iron bits, shown under the equipment in the "Character" tab.</summary>
    public InventoryViewModel Inventory { get; }

    /// <summary>"Waypoints" tab.</summary>
    public WaypointsViewModel Waypoints { get; }

    /// <summary>Something was changed that is not written to the save file yet.</summary>
    [ObservableProperty]
    private bool _hasUnsavedChanges;

    /// <summary>
    /// The bytes to write into <c>player.gdc</c>: the file as read with the changes made in the tabs (the character
    /// panel, the iron bits, the items moved, added, changed or removed in the bags, the items equipped, changed or
    /// taken off, and the waypoints unlocked or locked).
    /// </summary>
    /// <exception cref="System.IO.InvalidDataException">The changes cannot be written into this file.</exception>
    public byte[] GetSaveData() =>
        _saveEditor.Apply(Character, new CharacterChanges(
            Money: Inventory.ChangedMoney, Bags: Inventory.ChangedBags, Equipment: Equipment.ChangedEquipment,
            Profile: Profile.Changes, Waypoints: Waypoints.Changes));

    /// <summary>Call when a tab changed something, so the Save button follows the actual difference to the file.</summary>
    public void RefreshHasUnsavedChanges() =>
        HasUnsavedChanges = Profile.HasChanges || Equipment.HasChanges || Inventory.HasChanges || Waypoints.HasChanges;
}
