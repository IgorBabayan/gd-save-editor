namespace GD.SaveEditor.Presentation.ViewModels.Database;

public enum DatabaseGeneratorStepState
{
    Running,
    Completed,
    Stopped
}

/// <summary>
/// The progress bar of the database generator. There is only one: each step (download, read, migrate, ...)
/// resets it with <see cref="Start"/>, so the dialog keeps its size however many steps a run has.
/// </summary>
public sealed partial class DatabaseGeneratorStepViewModel : ObservableObject
{
    /// <summary>Generator id of the step the bar shows; reports of other steps are ignored.</summary>
    public int Id { get; private set; }

    /// <summary>1 for the first step of the run, then counting up.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderText))]
    private int _number;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderText))]
    private string _title = string.Empty;

    /// <summary>"Step 4 · Reading GDX1.arz".</summary>
    public string HeaderText => Number > 0 ? $"Step {Number} · {Title}" : Title;

    /// <summary>Progress from 0 to 100.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PercentText))]
    private double _percent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    private string? _detail;

    [ObservableProperty]
    private DatabaseGeneratorStepState _state = DatabaseGeneratorStepState.Running;

    public string PercentText => $"{Percent:0.0}%";

    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);

    /// <summary>Resets the bar to 0% for a new step.</summary>
    public void Start(int id, string title)
    {
        Id = id;
        Number++;
        Title = title;
        Percent = 0;
        Detail = null;
        State = DatabaseGeneratorStepState.Running;
    }

    public void Update(double fraction, string? detail)
    {
        Percent = fraction * 100;
        Detail = detail;
    }

    public void Finish(double fraction, string? detail, bool completed)
    {
        Update(fraction, detail);
        State = completed ? DatabaseGeneratorStepState.Completed : DatabaseGeneratorStepState.Stopped;
    }
}
