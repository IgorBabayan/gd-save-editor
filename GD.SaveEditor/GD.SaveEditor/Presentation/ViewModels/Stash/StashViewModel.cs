namespace GD.SaveEditor.Presentation.ViewModels.Stash;

/// <summary>
/// "Stash" tab of a character: its personal stash (tabs 1 to 8, the ones not bought locked) and the shared stash of its
/// mode, which every character of that mode shows (the main window keeps one per mode, see <c>MainViewModel</c>).
/// </summary>
public sealed partial class StashViewModel(PersonalStashViewModel personal, TransferStashViewModel? transfer) : ViewModelBase
{
    public PersonalStashViewModel Personal { get; } = personal;

    /// <summary>The shared stash; <c>null</c> while the save folder was not read. Replaced when its file is read again.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTransfer))]
    private TransferStashViewModel? _transfer = transfer;

    public bool HasTransfer => Transfer is not null;
}
