using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Presentation.ViewModels.Inventory;

/// <summary>"Inventory" tab of the selected character. Not implemented yet: shows an empty page.</summary>
public sealed class InventoryViewModel(CharacterSave character) : ViewModelBase
{
    public CharacterSave Character { get; } = character;

    /// <summary>Nothing can be edited yet.</summary>
    public bool HasChanges => false;
}
