namespace GD.SaveEditor.Presentation.ViewModels.Stash;

/// <summary>
/// "Stash" tab of a character: its personal stash (tabs 1 to 8, the ones not bought locked), the shared stash and the
/// Smuggler's material storage of its mode, which every character of that mode shows (the main window keeps one of each
/// per mode, see <c>MainViewModel</c>).
/// </summary>
public sealed partial class StashViewModel(
    PersonalStashViewModel personal, TransferStashViewModel? transfer, MaterialStorageViewModel? materials) : ViewModelBase
{
    public PersonalStashViewModel Personal { get; } = personal;

    /// <summary>The shared stash; <c>null</c> while the save folder was not read. Replaced when its file is read again.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTransfer))]
    private TransferStashViewModel? _transfer = transfer;

    public bool HasTransfer => Transfer is not null;

    /// <summary>
    /// Components and crafting materials stored at the Smuggler (game version 1.3+); <c>null</c> while the save folder
    /// was not read. Replaced when its file is read again.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMaterials))]
    private MaterialStorageViewModel? _materials = materials;

    public bool HasMaterials => Materials is not null;
}
