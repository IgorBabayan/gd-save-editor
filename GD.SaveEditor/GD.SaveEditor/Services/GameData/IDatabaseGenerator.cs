namespace GD.SaveEditor.Services.GameData;

/// <summary>
/// Generates or updates <c>%APPDATA%\GD.SaveEditor\database.db</c>: gets the game files (GrimTools, else the game
/// folder), reads them and applies a migration holding only what the database does not have yet.
/// </summary>
public interface IDatabaseGenerator
{
    /// <summary>
    /// Runs the whole generation in the background and returns when it is done.
    /// Never throws for generation problems: they are returned as a <see cref="DatabaseGenerationStatus.Failed"/> result.
    /// </summary>
    /// <param name="progress">
    /// Receives every progress bar and text line, in order. Create it on the UI thread to get the reports there.
    /// </param>
    /// <param name="cancellationToken">Stops the generation; the database stays as it was.</param>
    Task<DatabaseGenerationResult> RunAsync(
        IProgress<DatabaseGeneratorEvent>? progress,
        CancellationToken cancellationToken = default);
}
