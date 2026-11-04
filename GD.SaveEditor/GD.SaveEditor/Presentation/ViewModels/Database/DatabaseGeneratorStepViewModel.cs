namespace GD.SaveEditor.Presentation.ViewModels.Database;

public enum DatabaseGeneratorStepState
{
    Running,
    Completed,
    Stopped
}

/// <summary>One progress bar of the database generator (download, read, migrate, ...).</summary>
public sealed partial class DatabaseGeneratorStepViewModel(int id, string title) : ObservableObject
{
    public int Id { get; } = id;

    public string Title { get; } = title;

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
