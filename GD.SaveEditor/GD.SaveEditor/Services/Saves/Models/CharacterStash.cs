namespace GD.SaveEditor.Services.Saves.Models;

/// <summary>An item in a stash tab at its top-left cell; the game stores the cell as two floats.</summary>
public sealed record StashItem(SaveItem Item, float X, float Y);

/// <summary>One stash tab (a "sack" in the game's files): its size in cells and the items in it.</summary>
public sealed record StashTab(int Width, int Height, IReadOnlyList<StashItem> Items);

/// <summary>
/// The personal stash of a character (block 4 of <c>player.gdc</c>). The file holds one tab per tab the character
/// bought from the stash; tabs that were not bought yet are not in the file.
/// </summary>
public sealed record CharacterStash(IReadOnlyList<StashTab> Tabs)
{
    /// <summary>Tabs the game lets a character buy for the personal stash.</summary>
    public const int MaxTabs = 8;

    /// <summary>Size of a tab in the current game (used for the tabs that were not bought yet).</summary>
    public const int DefaultWidth = 10;

    public const int DefaultHeight = 19;
}
