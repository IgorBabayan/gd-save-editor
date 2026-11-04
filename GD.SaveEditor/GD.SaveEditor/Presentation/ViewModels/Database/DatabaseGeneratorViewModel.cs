using System.Collections.ObjectModel;
using GD.SaveEditor.Services.GameData;
using GD.SaveEditor.Services.GameData.Database;
using GD.SaveEditor.Services.GameData.Textures;
using GD.SaveEditor.Services.Notifications;

namespace GD.SaveEditor.Presentation.ViewModels.Database;

/// <summary>
/// Progress dialog of the database generator. Mirrors every progress bar the generator reports (downloads,
/// reading the game files, the migration), cannot be closed while it runs and enables OK once it has finished.
/// </summary>
public sealed partial class DatabaseGeneratorViewModel(
    IDatabaseGenerator generator,
    IGameTextProvider gameTextProvider,
    IGameItemProvider gameItemProvider,
    IGameIconProvider gameIconProvider) : ViewModelBase
{
    private readonly Dictionary<int, DatabaseGeneratorStepViewModel> _stepsById = [];
    private Task? _run;

    /// <summary>Progress bars in the order the generator started them.</summary>
    public ObservableCollection<DatabaseGeneratorStepViewModel> Steps { get; } = [];

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
                GetOrAddStep(started.Id, started.Title);
                break;

            case DatabaseGeneratorStepProgress step:
                GetOrAddStep(step.Id).Update(step.Fraction, step.Detail);
                break;

            case DatabaseGeneratorStepFinished finished:
                GetOrAddStep(finished.Id).Finish(finished.Fraction, finished.Detail, finished.Completed);
                break;

            case DatabaseGeneratorMessageReceived received:
                AddMessage(received.Message);
                break;
        }
    }

    private DatabaseGeneratorStepViewModel GetOrAddStep(int id, string? title = null)
    {
        if (_stepsById.TryGetValue(id, out var step))
            return step;

        step = new DatabaseGeneratorStepViewModel(id, title ?? "Working");
        _stepsById.Add(id, step);
        Steps.Add(step);
        return step;
    }

    private void AddMessage(DatabaseGeneratorMessage message)
    {
        Messages.Add(message);
        OnPropertyChanged(nameof(HasMessages));
    }
}
