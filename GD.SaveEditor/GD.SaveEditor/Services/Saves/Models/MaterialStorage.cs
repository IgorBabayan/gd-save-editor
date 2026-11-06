namespace GD.SaveEditor.Services.Saves.Models;

/// <summary>An amount of one material in the Smuggler's material storage.</summary>
/// <param name="RecordPath">E.g. <c>records/items/materia/compa_aethercrystal.dbr</c>.</param>
public sealed record StoredMaterial(string RecordPath, int Amount);

/// <summary>
/// The Smuggler's material storage of softcore or hardcore characters (the shared "Components" and "Crafting
/// Materials" pages of game version 1.3+), read from <c>reagents.gst</c> / <c>reagents.gsh</c>.
/// </summary>
public sealed record MaterialStorage
{
    /// <summary>Largest amount the editor writes for one material.</summary>
    public const int MaxAmount = 999_999;

    public required string FilePath { get; init; }

    public required bool Hardcore { get; init; }

    /// <summary>Stored materials in the file's order (materials the file does not list are 0).</summary>
    public required IReadOnlyList<StoredMaterial> Materials { get; init; }

    public required byte[] RawData { get; init; }

    public required long FileLength { get; init; }

    public required DateTime FileLastWriteUtc { get; init; }
}

/// <summary>What was found for one mode: the storage, or why there is none (the file is missing or could not be read).</summary>
public sealed record MaterialStorageLoad(bool Hardcore, string? FilePath, MaterialStorage? Storage, string? Error);
