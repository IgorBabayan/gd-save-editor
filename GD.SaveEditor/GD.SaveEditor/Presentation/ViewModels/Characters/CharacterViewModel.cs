using GD.SaveEditor.Presentation.Services;
using GD.SaveEditor.Presentation.ViewModels.Inventory;
using GD.SaveEditor.Presentation.ViewModels.Waypoints;
using GD.SaveEditor.Services.GameData.Database;
using GD.SaveEditor.Services.GameData.Textures;
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
        IDialogService dialogs)
    {
        _saveEditor = saveEditor;
        Character = character;
        Equipment = new CharacterEquipmentViewModel(character, items, icons);
        Inventory = new InventoryViewModel(character, items, icons, dialogs);
        Waypoints = new WaypointsViewModel(character);

        Inventory.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(InventoryViewModel.HasChanges))
                RefreshHasUnsavedChanges();
        };

        RefreshHasUnsavedChanges();
    }

    public CharacterSave Character { get; }

    /// <summary>"Character" tab: equipment and attributes.</summary>
    public CharacterEquipmentViewModel Equipment { get; }

    /// <summary>Bags and iron bits, shown under the equipment in the "Character" tab.</summary>
    public InventoryViewModel Inventory { get; }

    /// <summary>"Waypoints" tab.</summary>
    public WaypointsViewModel Waypoints { get; }

    /// <summary>Something was changed that is not written to the save file yet.</summary>
    [ObservableProperty]
    private bool _hasUnsavedChanges;

    /// <summary>
    /// The bytes to write into <c>player.gdc</c>: the file as read with the changes made in the tabs (the iron bits
    /// and the items moved, added, changed or removed in the bags).
    /// </summary>
    /// <exception cref="System.IO.InvalidDataException">The changes cannot be written into this file.</exception>
    public byte[] GetSaveData() =>
        _saveEditor.Apply(Character, new CharacterChanges(Money: Inventory.ChangedMoney, Bags: Inventory.ChangedBags));

    /// <summary>Call when a tab changed something, so the Save button follows the actual difference to the file.</summary>
    public void RefreshHasUnsavedChanges() =>
        HasUnsavedChanges = Equipment.HasChanges || Inventory.HasChanges || Waypoints.HasChanges;
}
