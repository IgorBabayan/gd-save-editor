using GD.SaveEditor.Presentation.Services;
using GD.SaveEditor.Presentation.ViewModels.Stash;
using GD.SaveEditor.Services.GameData.Database;
using GD.SaveEditor.Services.GameData.Items;
using GD.SaveEditor.Services.GameData.Textures;
using GD.SaveEditor.Services.GameData.Waypoints;
using GD.SaveEditor.Services.Saves;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Presentation.ViewModels.Characters;

/// <summary>Creates the details of a character with the game data services they need.</summary>
public sealed class CharacterViewModelFactory(
    IGameItemProvider items,
    IGameIconProvider icons,
    ICharacterSaveEditor saveEditor,
    IDialogService dialogs,
    IGameWaypointProvider waypoints,
    IGameTextProvider texts,
    IItemCatalog catalog)
{
    /// <param name="transferStash">The shared stash of the character's mode; <c>null</c> while it was not read.</param>
    /// <param name="materialStorage">The material storage of the character's mode; <c>null</c> while it was not read.</param>
    public CharacterViewModel Create(
        CharacterSave character, TransferStashViewModel? transferStash, MaterialStorageViewModel? materialStorage) =>
        new(character, items, icons, saveEditor, dialogs, waypoints, texts, transferStash, materialStorage);

    /// <summary>The shared stash of a mode, as read from its file.</summary>
    public TransferStashViewModel CreateTransferStash(TransferStashLoad load) => new(load, items, icons, dialogs);

    /// <summary>The material storage of a mode, as read from its file.</summary>
    public MaterialStorageViewModel CreateMaterialStorage(MaterialStorageLoad load) => new(load, items, icons, catalog);
}
