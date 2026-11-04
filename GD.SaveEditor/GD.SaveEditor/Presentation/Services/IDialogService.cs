using GD.SaveEditor.Presentation.ViewModels.Inventory;
using GD.SaveEditor.Services.GameData;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Presentation.Services;

/// <summary>What the item builder ended with.</summary>
/// <param name="Item">The built item; <c>null</c> when nothing is to be put in the bag (cancelled or removed).</param>
/// <param name="Removed">The user removed the edited item.</param>
public sealed record ItemBuilderResult(BuiltItem? Item, bool Removed);

public interface IDialogService
{
    /// <summary>Shows the modal Settings dialog.</summary>
    /// <returns><c>true</c> if the user saved the settings.</returns>
    bool ShowSettings();

    /// <summary>Shows a modal Yes/No question.</summary>
    /// <returns><c>true</c> if the user answered Yes.</returns>
    bool Confirm(string title, string message);

    /// <summary>
    /// Shows the modal database generator dialog: downloads the game files, migrates <c>database.db</c>,
    /// shows the progress bars and returns after the user pressed OK.
    /// </summary>
    /// <returns>The generator's outcome, or <c>null</c> if it did not finish.</returns>
    DatabaseGenerationResult? ShowDatabaseGenerator();

    /// <summary>
    /// Shows the modal item builder for a new bag item (<paramref name="current"/> <c>null</c>) or to change one.
    /// </summary>
    ItemBuilderResult ShowItemBuilder(SaveItem? current);

    /// <summary>
    /// Shows the compact item builder (the inspector) next to <paramref name="anchor"/> (a view element) for a new bag
    /// item (<paramref name="current"/> <c>null</c>) or to change one, and returns at once: <paramref name="applied"/>
    /// is called with the built item when the user applies it, <paramref name="removed"/> when the user removes the
    /// item. Its expand button opens the full builder window. Without an anchor the full window opens (modal).
    /// </summary>
    void ShowItemInspector(object? anchor, SaveItem? current, Action<BuiltItem> applied, Action? removed = null);

    /// <summary>Closes the item inspector without applying it, if one is open.</summary>
    void CloseItemInspector();
}
