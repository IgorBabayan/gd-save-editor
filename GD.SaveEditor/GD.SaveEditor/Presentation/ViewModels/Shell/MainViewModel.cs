using System.Collections.ObjectModel;
using System.IO;
using GD.SaveEditor.Presentation.Services;
using GD.SaveEditor.Presentation.Services.Toasts;
using GD.SaveEditor.Presentation.ViewModels.Characters;
using GD.SaveEditor.Presentation.ViewModels.Notifications;
using GD.SaveEditor.Services.GameData;
using GD.SaveEditor.Services.Notifications;
using GD.SaveEditor.Services.Saves;
using GD.SaveEditor.Services.Saves.Models;
using GD.SaveEditor.Services.Settings;

namespace GD.SaveEditor.Presentation.ViewModels.Shell;

public partial class MainViewModel(
    IDialogService dialogService,
    ISettingsService settingsService,
    ISaveFolderLoader saveFolderLoader,
    ICharacterSaveWriter characterSaveWriter,
    INotificationService notificationService,
    IToastService toastService,
    NotificationCenterViewModel notificationCenter) : ViewModelBase
{
    private const int SoftcoreTabIndex = 0;
    private const int HardcoreTabIndex = 1;

    private CancellationTokenSource? _loadCts;

    /// <summary>
    /// Set while the tab lists are rebuilt: clearing a list makes its ListBox push a <c>null</c> selection,
    /// which must not close the details of the selected character.
    /// </summary>
    private bool _isRefreshingLists;

    /// <summary>Character <see cref="CharacterDetails"/> was created for.</summary>
    private CharacterSave? _detailsCharacter;

    /// <summary>
    /// Details of every character opened since the folder was read, by file path, so changes that are not saved yet
    /// survive selecting another character.
    /// </summary>
    private readonly Dictionary<string, CharacterViewModel> _openedCharacters = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Bell button and notification popup in the title bar.</summary>
    public NotificationCenterViewModel NotificationCenter { get; } = notificationCenter;

    /// <summary>Characters of the selected save folder, by name.</summary>
    public ObservableCollection<CharacterSave> Characters { get; } = [];

    /// <summary>Softcore characters shown in the Softcore tab (after the SSF filter).</summary>
    public ObservableCollection<CharacterSave> SoftcoreCharacters { get; } = [];

    /// <summary>Hardcore characters shown in the Hardcore tab (after the SSF filter).</summary>
    public ObservableCollection<CharacterSave> HardcoreCharacters { get; } = [];

    /// <summary>Character files that could not be read.</summary>
    public ObservableCollection<SaveFileError> LoadErrors { get; } = [];

    /// <summary>Open tab of the character list: 0 = Softcore, 1 = Hardcore.</summary>
    [ObservableProperty]
    private int _selectedCharacterTabIndex = SoftcoreTabIndex;

    /// <summary>Selection of the Softcore tab (kept while the Hardcore tab is open).</summary>
    [ObservableProperty]
    private CharacterSave? _selectedSoftcoreCharacter;

    /// <summary>Selection of the Hardcore tab (kept while the Softcore tab is open).</summary>
    [ObservableProperty]
    private CharacterSave? _selectedHardcoreCharacter;

    /// <summary>"Show only SSF characters" checkbox of the Softcore tab.</summary>
    [ObservableProperty]
    private bool _showOnlySsfSoftcore;

    /// <summary>"Show only SSF characters" checkbox of the Hardcore tab.</summary>
    [ObservableProperty]
    private bool _showOnlySsfHardcore;

    /// <summary>Selected character of the open tab; its details are shown on the right.</summary>
    public CharacterSave? SelectedCharacter =>
        SelectedCharacterTabIndex == HardcoreTabIndex ? SelectedHardcoreCharacter : SelectedSoftcoreCharacter;

    /// <summary>Details (Inventory / Waypoints tabs) of <see cref="SelectedCharacter"/>; <c>null</c> when nothing is selected.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReloadCommand))]
    private CharacterViewModel? _characterDetails;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoCharacters))]
    [NotifyPropertyChangedFor(nameof(ShowLoading))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReloadCommand))]
    private bool _isLoading;

    /// <summary>The selected character is being written to its save file.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReloadCommand))]
    private bool _isSaving;

    /// <summary>Some opened character has changes that are not saved yet.</summary>
    public bool HasUnsavedChanges => _openedCharacters.Values.Any(c => c.HasUnsavedChanges);

    public bool HasSaveFolder => IsExistingFolder(settingsService.Current.SaveFolderPath);
    public bool ShowNoSaveFolder => !HasSaveFolder;
    public bool HasCharacters => Characters.Count > 0;
    public bool HasSelectedCharacter => SelectedCharacter is not null;
    public bool HasVisibleSoftcoreCharacters => SoftcoreCharacters.Count > 0;
    public bool HasVisibleHardcoreCharacters => HardcoreCharacters.Count > 0;
    public bool HasLoadErrors => LoadErrors.Count > 0;

    /// <summary>A folder is selected and loaded, but it holds no readable characters.</summary>
    public bool ShowNoCharacters => HasSaveFolder && !IsLoading && !HasCharacters;

    /// <summary>Loading message is shown only while the list is still empty (a reload keeps the old list visible).</summary>
    public bool ShowLoading => IsLoading && !HasCharacters;

    public bool ShowSelectCharacterHint => HasCharacters && !HasSelectedCharacter;

    partial void OnSelectedCharacterTabIndexChanged(int value) => UpdateSelectedCharacter();

    partial void OnSelectedSoftcoreCharacterChanged(CharacterSave? value)
    {
        if (!_isRefreshingLists)
            UpdateSelectedCharacter();
    }

    partial void OnSelectedHardcoreCharacterChanged(CharacterSave? value)
    {
        if (!_isRefreshingLists)
            UpdateSelectedCharacter();
    }

    partial void OnShowOnlySsfSoftcoreChanged(bool value) => RefreshCharacterLists();

    partial void OnShowOnlySsfHardcoreChanged(bool value) => RefreshCharacterLists();

    /// <summary>Recreates <see cref="CharacterDetails"/> when the selected character of the open tab changes.</summary>
    private void UpdateSelectedCharacter()
    {
        var selected = SelectedCharacter;
        if (!ReferenceEquals(selected, _detailsCharacter))
        {
            _detailsCharacter = selected;
            CharacterDetails = selected is null ? null : GetDetails(selected);
        }

        OnPropertyChanged(nameof(SelectedCharacter));
        OnPropertyChanged(nameof(HasSelectedCharacter));
        OnPropertyChanged(nameof(ShowSelectCharacterHint));
    }

    /// <summary>
    /// Splits <see cref="Characters"/> into the Softcore / Hardcore tabs, applying each tab's SSF filter.
    /// Each tab keeps its selected character if it is still listed, otherwise its first character is selected.
    /// </summary>
    private void RefreshCharacterLists()
    {
        var previousSoftcore = SelectedSoftcoreCharacter?.FilePath;
        var previousHardcore = SelectedHardcoreCharacter?.FilePath;

        _isRefreshingLists = true;
        try
        {
            Replace(SoftcoreCharacters, Characters.Where(c => !c.Hardcore && (!ShowOnlySsfSoftcore || c.SoloSelfFound)));
            Replace(HardcoreCharacters, Characters.Where(c => c.Hardcore && (!ShowOnlySsfHardcore || c.SoloSelfFound)));

            SelectedSoftcoreCharacter = FindByPath(SoftcoreCharacters, previousSoftcore) ?? SoftcoreCharacters.FirstOrDefault();
            SelectedHardcoreCharacter = FindByPath(HardcoreCharacters, previousHardcore) ?? HardcoreCharacters.FirstOrDefault();
        }
        finally
        {
            _isRefreshingLists = false;
        }

        OnPropertyChanged(nameof(HasVisibleSoftcoreCharacters));
        OnPropertyChanged(nameof(HasVisibleHardcoreCharacters));
        UpdateSelectedCharacter();
    }

    /// <summary>The details of <paramref name="character"/>, with its unsaved changes if it was opened before.</summary>
    private CharacterViewModel GetDetails(CharacterSave character)
    {
        if (_openedCharacters.TryGetValue(character.FilePath, out var opened) && ReferenceEquals(opened.Character, character))
            return opened;

        var details = new CharacterViewModel(character);
        details.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(CharacterViewModel.HasUnsavedChanges))
                return;

            OnPropertyChanged(nameof(HasUnsavedChanges));
            SaveCommand.NotifyCanExecuteChanged();
        };

        _openedCharacters[character.FilePath] = details;
        return details;
    }

    /// <summary>Forgets opened details whose character is no longer listed (the folder was read again).</summary>
    private void ForgetClosedCharacters()
    {
        foreach (var (path, details) in _openedCharacters.ToList())
        {
            if (!Characters.Any(c => ReferenceEquals(c, details.Character)))
                _openedCharacters.Remove(path);
        }

        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    /// <summary>Asks before something would throw away unsaved changes of <paramref name="characters"/>; <c>true</c> when it may go ahead.</summary>
    private bool ConfirmDiscardChanges(string action, IEnumerable<CharacterViewModel>? characters = null)
    {
        var names = (characters ?? _openedCharacters.Values)
            .Where(c => c.HasUnsavedChanges)
            .Select(c => c.Character.CharacterName)
            .ToList();
        if (names.Count == 0)
            return true;

        return dialogService.Confirm(
            "Unsaved changes",
            $"{string.Join(", ", names)} {(names.Count == 1 ? "has" : "have")} unsaved changes. {action} and discard them?");
    }

    /// <summary>Called when the main window is about to close; <c>false</c> keeps it open.</summary>
    public bool ConfirmClose() => ConfirmDiscardChanges("Close the editor");

    private bool CanSave() => !IsSaving && !IsLoading && CharacterDetails?.HasUnsavedChanges == true;

    /// <summary>
    /// Title bar save button (Ctrl+S): writes the selected character into its <c>player.gdc</c>. The previous file is
    /// kept as a backup, and the character is read back from the written file, so what is shown is what the game loads.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (CharacterDetails is not { } details)
            return;

        var character = details.Character;
        IsSaving = true;
        try
        {
            var result = await characterSaveWriter.SaveAsync(character, details.GetSaveData());
            ReplaceCharacter(character, result.Character);

            toastService.Show(
                $"{character.CharacterName} saved. The previous file was kept as {Path.GetFileName(result.BackupPath)}.",
                NotificationSeverity.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // SaveFileChangedException is an IOException: the game (or something else) wrote the file meanwhile.
            notificationService.Notify($"{character.CharacterName} could not be saved", ex.Message, NotificationSeverity.Error);
            toastService.Show($"{character.CharacterName} could not be saved. See the notifications for details.", NotificationSeverity.Error);
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>Puts <paramref name="updated"/> (read again from its file) in place of <paramref name="previous"/> and shows it.</summary>
    private void ReplaceCharacter(CharacterSave previous, CharacterSave updated)
    {
        for (var i = 0; i < Characters.Count; i++)
        {
            if (ReferenceEquals(Characters[i], previous))
            {
                Characters[i] = updated;
                break;
            }
        }

        // The details are rebuilt from the file; the lists select the new instance by its path.
        _openedCharacters.Remove(previous.FilePath);
        RefreshCharacterLists();
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private static CharacterSave? FindByPath(IEnumerable<CharacterSave> characters, string? filePath) =>
        filePath is null
            ? null
            : characters.FirstOrDefault(c => string.Equals(c.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

    /// <summary>Called once when the main window opens: loads characters if the save folder exists.</summary>
    public async Task InitializeAsync()
    {
        if (HasSaveFolder)
            await LoadSavesAsync();
        else
            NotifyStateChanged();
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        // Re-read the folder whenever the user presses Save in the settings dialog.
        if (dialogService.ShowSettings())
            await LoadSavesAsync();
    }

    /// <summary>
    /// Title bar database button: asks for confirmation, then runs the generator in the progress dialog (download of
    /// the game files and a migration of <c>database.db</c> with only what it does not have yet). Once the dialog is
    /// closed, the outcome goes to the notification center and the characters are read again for their class names.
    /// </summary>
    [RelayCommand]
    private async Task GenerateDatabaseAsync()
    {
        if (!dialogService.Confirm("Database", "Are you sure to generate/update Database?"))
            return;

        var result = dialogService.ShowDatabaseGenerator();
        if (result is null)
            return;

        NotifyDatabaseResult(result);

        if (result.Status == DatabaseGenerationStatus.Succeeded && HasSaveFolder)
            await LoadSavesAsync();
    }

    private void NotifyDatabaseResult(DatabaseGenerationResult result)
    {
        var hasWarnings = result.Messages.Any(m => m.Level == DatabaseGeneratorMessageLevel.Warning);

        var (title, severity) = result.Status switch
        {
            DatabaseGenerationStatus.Succeeded when hasWarnings => ("Database updated with warnings", NotificationSeverity.Warning),
            DatabaseGenerationStatus.Succeeded => ("Database updated", NotificationSeverity.Success),
            DatabaseGenerationStatus.Cancelled => ("Database update cancelled", NotificationSeverity.Warning),
            _ => ("Database update failed", NotificationSeverity.Error)
        };

        var message = string.Join(Environment.NewLine, result.Messages.Select(m => m.Text));
        notificationService.Notify(title, string.IsNullOrWhiteSpace(message) ? null : message, severity);
    }

    private bool CanReload() => HasSaveFolder && !IsLoading && !IsSaving;

    /// <summary>
    /// Title bar reload button (F5): reads the save file of the selected character again (e.g. after the game wrote
    /// it); asks first when it has unsaved changes. Without a selected character the whole folder is read again.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanReload))]
    private async Task ReloadAsync()
    {
        if (SelectedCharacter is not { } character)
        {
            if (await LoadSavesAsync())
                toastService.Show("Save folder reloaded.", NotificationSeverity.Success);
            return;
        }

        CharacterViewModel[] opened = _openedCharacters.TryGetValue(character.FilePath, out var details) ? [details] : [];
        if (!ConfirmDiscardChanges($"Reload {character.CharacterName}", opened))
            return;

        IsLoading = true;
        try
        {
            var reloaded = await saveFolderLoader.LoadCharacterAsync(character.FilePath);
            ReplaceCharacter(character, reloaded);

            // A hardcore character that died, or a changed mode, moves it to the other tab: follow it.
            SelectedCharacterTabIndex = reloaded.Hardcore ? HardcoreTabIndex : SoftcoreTabIndex;
            toastService.Show($"{reloaded.CharacterName} reloaded.", NotificationSeverity.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            notificationService.Notify($"{character.CharacterName} could not be reloaded", $"{character.FilePath}: {ex.Message}", NotificationSeverity.Error);
            toastService.Show($"{character.CharacterName} could not be reloaded. See the notifications for details.", NotificationSeverity.Error);
        }
        finally
        {
            IsLoading = false;
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// Reads the save folder; <c>false</c> when the user kept the unsaved changes, a newer load superseded this one or
    /// the folder could not be read.
    /// </summary>
    private async Task<bool> LoadSavesAsync()
    {
        if (!ConfirmDiscardChanges("Read the save folder again"))
            return false;

        // A newer load (e.g. Save pressed again in Settings) supersedes the running one.
        _loadCts?.Cancel();
        using var cts = new CancellationTokenSource();
        _loadCts = cts;

        var folder = settingsService.Current.SaveFolderPath ?? string.Empty;
        var previousSelection = SelectedCharacter?.FilePath;

        var loaded = false;
        IsLoading = true;
        try
        {
            var content = await saveFolderLoader.LoadAsync(folder, cts.Token);
            if (cts.IsCancellationRequested)
                return false;

            Replace(Characters, content.Characters.OrderBy(c => c.CharacterName, StringComparer.CurrentCultureIgnoreCase));
            ForgetClosedCharacters();
            Replace(LoadErrors, content.Errors);

            if (content.Errors.Count > 0)
                NotifyLoadErrors(content.Errors);

            loaded = true;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Superseded by a newer load.
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Folder disappeared or is not accessible.
            Characters.Clear();
            ForgetClosedCharacters();
            LoadErrors.Clear();
            LoadErrors.Add(new SaveFileError(folder, ex.Message));

            notificationService.Notify("Save folder could not be read", $"{folder}: {ex.Message}", NotificationSeverity.Error);
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                _loadCts = null;
                IsLoading = false;
            }
        }

        // Each tab keeps its selected character after a reload, otherwise selects its first one.
        RefreshCharacterLists();

        // Stay on the tab of the previously selected character; otherwise open a tab that has characters.
        if (FindByPath(Characters, previousSelection) is { } previous)
            SelectedCharacterTabIndex = previous.Hardcore ? HardcoreTabIndex : SoftcoreTabIndex;
        else if (SoftcoreCharacters.Count == 0 && HardcoreCharacters.Count > 0)
            SelectedCharacterTabIndex = HardcoreTabIndex;
        else if (HardcoreCharacters.Count == 0 && SoftcoreCharacters.Count > 0)
            SelectedCharacterTabIndex = SoftcoreTabIndex;

        NotifyStateChanged();
        return loaded;
    }

    private void NotifyLoadErrors(IReadOnlyCollection<SaveFileError> errors)
    {
        var title = errors.Count == 1
            ? "1 save file could not be read"
            : $"{errors.Count} save files could not be read";

        var message = string.Join(Environment.NewLine,
            errors.Select(e => $"{Path.GetFileName(Path.GetDirectoryName(e.FilePath))}: {e.Message}"));

        notificationService.Notify(title, message, NotificationSeverity.Warning);
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(HasSaveFolder));
        OnPropertyChanged(nameof(ShowNoSaveFolder));
        OnPropertyChanged(nameof(HasCharacters));
        OnPropertyChanged(nameof(HasLoadErrors));
        OnPropertyChanged(nameof(ShowNoCharacters));
        OnPropertyChanged(nameof(ShowLoading));
        OnPropertyChanged(nameof(ShowSelectCharacterHint));
        ReloadCommand.NotifyCanExecuteChanged();
    }

    private static bool IsExistingFolder(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }
}
