namespace GD.SaveEditor.Services.Saves.Models;

/// <summary>
/// The transfer (shared) stash of softcore or hardcore characters, read from <c>transfer.gst</c> / <c>transfer.gsh</c>.
/// Every character of that mode shares it.
/// </summary>
public sealed record TransferStash
{
    /// <summary>Full path of the file.</summary>
    public required string FilePath { get; init; }

    public required bool Hardcore { get; init; }

    public required int Version { get; init; }

    /// <summary>Sacks (tabs) bought for the shared stash, in the file's order.</summary>
    public required IReadOnlyList<StashTab> Sacks { get; init; }

    /// <summary>int32 fields stored after the stack count of every item.</summary>
    public required int ItemExtraFields { get; init; }

    /// <summary>File contents as read; written back with the changes (with a backup).</summary>
    public required byte[] RawData { get; init; }

    /// <summary>Size and time of the file when it was read, to detect that the game wrote it meanwhile.</summary>
    public required long FileLength { get; init; }

    public required DateTime FileLastWriteUtc { get; init; }

    /// <summary>"Shared stash (softcore)".</summary>
    public string DisplayName => Hardcore ? "Shared stash (hardcore)" : "Shared stash (softcore)";
}

/// <summary>
/// What was found for one mode: the stash, or why there is none (the file is missing or could not be read).
/// </summary>
/// <param name="FilePath">The file that was read or looked for; <c>null</c> when no save folder is known.</param>
public sealed record TransferStashLoad(bool Hardcore, string? FilePath, TransferStash? Stash, string? Error);
