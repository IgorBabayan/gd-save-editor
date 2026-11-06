using System.Collections.ObjectModel;
using GD.SaveEditor.Services.GameData;
using GD.SaveEditor.Services.GameData.Database;
using GD.SaveEditor.Services.GameData.Textures;
using GD.SaveEditor.Services.GameData.Waypoints;
using GD.SaveEditor.Services.Notifications;

namespace GD.SaveEditor.Presentation.ViewModels.Database;

/// <summary>
/// Progress dialog of the database generator. Shows the generator's steps (downloads, reading the game files, the
/// migration) on one progress bar that each new step resets, cannot be closed while it runs and enables OK once it
/// has finished.
/// </summary>
public sealed partial class DatabaseGeneratorViewModel(
    IDatabaseGenerator generator,
    IGameTextProvider gameTextProvider,
    IGameItemProvider gameItemProvider,
    IGameIconProvider gameIconProvider,
    IGameWaypointProvider gameWaypointProvider) : ViewModelBase
{
    private Task? _run;

    /// <summary>The single progress bar: the step the generator is working on, or the last one when it has finished.</summary>
    public DatabaseGeneratorStepViewModel Step { get; } = new();

    /// <summary>Whether the generator has started a step yet (the bar is hidden until then).</summary>
    [ObservableProperty]
    private bool _hasStep;

    /// <summary>Text lines the generator printed (results, warnings, errors).</summary>
    public ObservableCollection<DatabaseGeneratorMessage> Messages { get; } = [];

    /// <summary><c>true</c> from the moment the dialog opens until the generator has exited.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OkCommand))]
    private bool _isRunning = true;

    [ObservableProperty]
    private string _statusText = "Generating / updating the database…";

    [ObservableProperty]
    private NotificationSeverity _statusSeverity = NotificationSeverity.Info;

    /// <summary>Outcome of the run; <c>null</c> until the generator has exited.</summary>
    public DatabaseGenerationResult? Result { get; private set; }

    public bool HasMessages => Messages.Count > 0;

    /// <summary>Raised by OK; the window closes itself.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Starts the generator once; later calls return the same run.</summary>
    public Task StartAsync() => _run ??= RunAsync();

    private async Task RunAsync()
    {
        // Created on the UI thread, so every report is applied on the UI thread, in order.
        var progress = new Progress<DatabaseGeneratorEvent>(Apply);

        try
        {
            Result = await generator.RunAsync(progress);
        }
        catch (Exception ex)
        {
            // The generator service reports its own problems; this only guards against the unexpected.
            var message = new DatabaseGeneratorMessage(DatabaseGeneratorMessageLevel.Error, ex.Message);
            AddMessage(message);
            Result = new DatabaseGenerationResult(DatabaseGenerationStatus.Failed, [message]);
        }

        // New or changed texts (class names, ...), items and icons must be read from the new data again.
        gameTextProvider.ClearCache();
        gameItemProvider.ClearCache();
        gameIconProvider.ClearCache();
        gameWaypointProvider.ClearCache();

        (StatusText, StatusSeverity) = Result.Status switch
        {
            DatabaseGenerationStatus.Succeeded => ("Done. The database is up to date.", NotificationSeverity.Success),
            DatabaseGenerationStatus.Cancelled => ("Database generation was cancelled.", NotificationSeverity.Warning),
            _ => ("The database could not be generated/updated.", NotificationSeverity.Error)
        };

        IsRunning = false;
    }

    private bool CanOk() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanOk))]
    private void Ok() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Apply(DatabaseGeneratorEvent generatorEvent)
    {
        switch (generatorEvent)
        {
            case DatabaseGeneratorStepStarted started:
                Step.Start(started.Id, started.Title);
                HasStep = true;
                break;

            // Reports of a step that is no longer shown (e.g. failed together with the current one) are dropped.
            case DatabaseGeneratorStepProgress step when HasStep && step.Id == Step.Id:
                Step.Update(step.Fraction, step.Detail);
                break;

            case DatabaseGeneratorStepFinished finished when HasStep && finished.Id == Step.Id:
                Step.Finish(finished.Fraction, finished.Detail, finished.Completed);
                break;

            case DatabaseGeneratorMessageReceived received:
                AddMessage(received.Message);
                break;
        }
    }

    private void AddMessage(DatabaseGeneratorMessage message)
    {
        Messages.Add(message);
        OnPropertyChanged(nameof(HasMessages));
    }
}
