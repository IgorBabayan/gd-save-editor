using GD.SaveEditor.Services.GameData.Database;
using GD.SaveEditor.Services.GameData.Textures;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Presentation.ViewModels.Characters;

/// <summary>Creates the details of a character with the game data services they need.</summary>
public sealed class CharacterViewModelFactory(IGameItemProvider items, IGameIconProvider icons)
{
    public CharacterViewModel Create(CharacterSave character) => new(character, items, icons);
}
