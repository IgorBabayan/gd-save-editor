using System.ComponentModel;
using System.Globalization;
using System.IO;
using GD.SaveEditor.Presentation.Services;
using GD.SaveEditor.Services.Settings;
using GD.SaveEditor.Services.Theming;
using GD.SaveEditor.Services.Updates;

namespace GD.SaveEditor.Presentation.ViewModels.Settings;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IThemeService _themeService;
    private readonly IFolderPickerService _folderPicker;
    private readonly IAutoUpdater _autoUpdater;

    /// <summary>Theme that was active when the dialog opened; restored if the user cancels the live preview.</summary>
    private readonly AppTheme _originalTheme;
    private bool _isSaved;

    public SettingsViewModel(
        ISettingsService settingsService,
        IThemeService themeService,
        IFolderPickerService folderPicker,
        IUpdateService updateService,
        IAutoUpdater autoUpdater)
    {
        _settingsService = settingsService;
        _themeService = themeService;
        _folderPicker = folderPicker;
        _autoUpdater = autoUpdater;

        ThemeOptions =
        [
            CreateOption(AppTheme.DefaultLight, "Default light"),
            CreateOption(AppTheme.DefaultNight, "Default night"),
            CreateOption(AppTheme.NightOwl, "Night owl")
        ];

        var settings = settingsService.Current;
        _originalTheme = themeService.CurrentTheme;

        SelectedThemeOption = ThemeOptions.FirstOrDefault(o => o.Theme == settings.Theme)
                              ?? ThemeOptions.First(o => o.Theme == AppTheme.DefaultNight);

        // C:\Users\<user>\(OneDrive\)Documents\My Games\Grim Dawn\save\main unless another folder was chosen.
        SaveFolderPath = !string.IsNullOrWhiteSpace(settings.SaveFolderPath)
            ? settings.SaveFolderPath
            : GrimDawnPaths.DefaultSaveFolder;

        GameFolderPath = !string.IsNullOrWhiteSpace(settings.GameFolderPath)
            ? settings.GameFolderPath
            : GrimDawnPaths.FindGameFolder() ?? string.Empty;

        AutoUpdate = settings.AutoUpdate;
        CurrentVersionText = $"Current version: {updateService.CurrentVersion.ToString(3)}";
        IsNotInstalled = !updateService.IsInstalled;
        RestartDelayText = settings.RestartDelaySeconds.ToString(CultureInfo.InvariantCulture);

        // The updater is a singleton that outlives this dialog: unsubscribed in OnClosed.
        _autoUpdater.PropertyChanged += OnAutoUpdaterPropertyChanged;
    }

    /// <summary>Asks the view to close: <c>true</c> = saved, <c>false</c> = cancelled.</summary>
    public event EventHandler<bool>? CloseRequested;

    public IReadOnlyList<ThemeOption> ThemeOptions { get; }

    [ObservableProperty]
    private ThemeOption _selectedThemeOption = null!;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSaveFolderMissing))]
    private string _saveFolderPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGameFolderInvalid))]
    private string _gameFolderPath = string.Empty;

    /// <summary>Check GitHub for a new release on every start and install it automatically.</summary>
    [ObservableProperty]
    private bool _autoUpdate;

    public string CurrentVersionText { get; }

    /// <summary>Text of the "Seconds before auto-restart" box; digits only (see NumericInput).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRestartDelayInvalid))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(IncreaseRestartDelayCommand))]
    [NotifyCanExecuteChangedFor(nameof(DecreaseRestartDelayCommand))]
    private string _restartDelayText = string.Empty;

    public int MaxRestartDelaySeconds => AppSettings.MaxRestartDelaySeconds;

    public bool IsRestartDelayInvalid => !TryGetRestartDelay(out _);

    /// <summary>Result of the last check, shown under the update buttons.</summary>
    public string? AvailableUpdateText => _autoUpdater.AvailableUpdate is { } update
        ? $"Version {update.Version.ToString(3)} is available."
        : null;

    public bool HasAvailableUpdate => _autoUpdater.AvailableUpdate is not null;

    /// <summary>True for development builds / unpacked copies, which are never updated.</summary>
    public bool IsNotInstalled { get; }

    /// <summary>True when a path is entered but the folder does not exist.</summary>
    public bool IsSaveFolderMissing =>
        !string.IsNullOrWhiteSpace(SaveFolderPath) && !Directory.Exists(SaveFolderPath.Trim());

    /// <summary>True when a path is entered but it is not a Grim Dawn installation.</summary>
    public bool IsGameFolderInvalid =>
        !string.IsNullOrWhiteSpace(GameFolderPath) && !GrimDawnPaths.IsGameFolder(GameFolderPath);

    /// <summary>Live preview: apply the theme as soon as it is selected.</summary>
    partial void OnSelectedThemeOptionChanged(ThemeOption value) => _themeService.Apply(value.Theme);

    [RelayCommand]
    private void SelectFolder()
    {
        var initial = Directory.Exists(SaveFolderPath.Trim())
            ? SaveFolderPath.Trim()
            : GrimDawnPaths.DefaultSaveFolder;

        var folder = _folderPicker.PickFolder("Select Grim Dawn saves folder", initial);
        if (folder is not null)
            SaveFolderPath = folder;
    }

    [RelayCommand]
    private void SelectGameFolder()
    {
        var initial = Directory.Exists(GameFolderPath.Trim()) ? GameFolderPath.Trim() : null;

        var folder = _folderPicker.PickFolder("Select Grim Dawn installation folder", initial);
        if (folder is not null)
            GameFolderPath = folder;
    }

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private Task CheckForUpdatesAsync() => _autoUpdater.CheckForUpdatesAsync();

    private bool CanCheckForUpdates() => !_autoUpdater.IsBusy;

    /// <summary>Enabled once "Check for updates" (or the startup check) found a newer version.</summary>
    [RelayCommand(CanExecute = nameof(CanDownloadAndUpdate))]
    private Task DownloadAndUpdateAsync() =>
        // Use the value in the box even if Settings was not saved yet.
        _autoUpdater.DownloadAndInstallAsync(TryGetRestartDelay(out var seconds) ? seconds : null);

    private bool CanDownloadAndUpdate() =>
        _autoUpdater.AvailableUpdate is not null && _autoUpdater.CanInstall && !_autoUpdater.IsBusy;

    [RelayCommand(CanExecute = nameof(CanIncreaseRestartDelay))]
    private void IncreaseRestartDelay() =>
        RestartDelayText = (CurrentRestartDelay() + 1).ToString(CultureInfo.InvariantCulture);

    private bool CanIncreaseRestartDelay() => CurrentRestartDelay() < AppSettings.MaxRestartDelaySeconds;

    [RelayCommand(CanExecute = nameof(CanDecreaseRestartDelay))]
    private void DecreaseRestartDelay() =>
        RestartDelayText = (CurrentRestartDelay() - 1).ToString(CultureInfo.InvariantCulture);

    private bool CanDecreaseRestartDelay() => CurrentRestartDelay() > 0;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        var path = SaveFolderPath.Trim();
        var gamePath = GameFolderPath.Trim();
        TryGetRestartDelay(out var restartDelay);

        _settingsService.Save(_settingsService.Current with
        {
            Theme = SelectedThemeOption.Theme,
            SaveFolderPath = string.IsNullOrEmpty(path) ? null : path,
            GameFolderPath = string.IsNullOrEmpty(gamePath) ? null : gamePath,
            AutoUpdate = AutoUpdate,
            RestartDelaySeconds = restartDelay
        });

        _isSaved = true;
        CloseRequested?.Invoke(this, true);
    }

    private bool CanSave() => !IsRestartDelayInvalid;

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    /// <summary>Called by the view when the window closes by any means (Cancel, Esc, Alt+F4, ×).</summary>
    public void OnClosed()
    {
        _autoUpdater.PropertyChanged -= OnAutoUpdaterPropertyChanged;

        if (!_isSaved)
            _themeService.Apply(_originalTheme);
    }

    private void OnAutoUpdaterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(AvailableUpdateText));
        OnPropertyChanged(nameof(HasAvailableUpdate));
        CheckForUpdatesCommand.NotifyCanExecuteChanged();
        DownloadAndUpdateCommand.NotifyCanExecuteChanged();
    }

    private bool TryGetRestartDelay(out int seconds) =>
        int.TryParse(RestartDelayText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out seconds)
        && seconds is >= 0 and <= AppSettings.MaxRestartDelaySeconds;

    /// <summary>Current value for the +/- buttons; an empty or invalid box counts as the default.</summary>
    private int CurrentRestartDelay() =>
        int.TryParse(RestartDelayText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            ? Math.Clamp(seconds, 0, AppSettings.MaxRestartDelaySeconds)
            : new AppSettings().RestartDelaySeconds;

    private ThemeOption CreateOption(AppTheme theme, string displayName) =>
        new(theme, displayName, _themeService.GetPalette(theme));
}
