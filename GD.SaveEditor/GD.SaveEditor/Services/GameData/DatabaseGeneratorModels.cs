namespace GD.SaveEditor.Services.GameData;

/// <summary>How a run of the database generator ended.</summary>
public enum DatabaseGenerationStatus
{
    Succeeded,
    Failed,
    Cancelled
}

/// <summary>Importance of a text line printed by the generator.</summary>
public enum DatabaseGeneratorMessageLevel
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>A text line printed by the generator (e.g. "Migration ... applied: +3 added").</summary>
public sealed record DatabaseGeneratorMessage(DatabaseGeneratorMessageLevel Level, string Text);

/// <summary>Outcome of a generator run with everything it printed, in order.</summary>
public sealed record DatabaseGenerationResult(
    DatabaseGenerationStatus Status,
    IReadOnlyList<DatabaseGeneratorMessage> Messages);

/// <summary>Something the generator reported while running (see <see cref="IDatabaseGenerator.RunAsync"/>).</summary>
public abstract record DatabaseGeneratorEvent;

/// <summary>The generator started a new progress bar.</summary>
public sealed record DatabaseGeneratorStepStarted(int Id, string Title) : DatabaseGeneratorEvent;

/// <summary>A progress bar moved; <paramref name="Fraction"/> is between 0 and 1.</summary>
public sealed record DatabaseGeneratorStepProgress(int Id, double Fraction, string? Detail) : DatabaseGeneratorEvent;

/// <summary>A progress bar stopped: at 100% (<paramref name="Completed"/>) or where it failed.</summary>
public sealed record DatabaseGeneratorStepFinished(int Id, double Fraction, string? Detail, bool Completed)
    : DatabaseGeneratorEvent;

/// <summary>The generator printed a line of text.</summary>
public sealed record DatabaseGeneratorMessageReceived(DatabaseGeneratorMessage Message) : DatabaseGeneratorEvent;
