namespace GD.SaveEditor.Services.Saves.Models;

/// <summary>
/// The waypoints (rift gates) a character has unlocked, per difficulty: block 6 of <c>player.gdc</c>.
/// </summary>
/// <param name="ByDifficulty">
/// Normal, Elite and Ultimate (index = <see cref="GameDifficulty"/>): the ids of the unlocked gates in the order the
/// file stores them, as 32 lowercase hex digits (see <c>GameWaypoint.Uid</c>).
/// </param>
public sealed record CharacterWaypoints(IReadOnlyList<IReadOnlyList<string>> ByDifficulty)
{
    /// <summary>The save file has a list for each of Normal, Elite and Ultimate.</summary>
    public const int DifficultyCount = 3;

    public IReadOnlyList<string> this[GameDifficulty difficulty] => ByDifficulty[(int)difficulty];

    /// <summary>Whether <paramref name="uid"/> is 32 hex digits, as a waypoint id must be.</summary>
    public static bool IsValidUid(string? uid) =>
        uid is { Length: 32 } && uid.All(Uri.IsHexDigit);
}
