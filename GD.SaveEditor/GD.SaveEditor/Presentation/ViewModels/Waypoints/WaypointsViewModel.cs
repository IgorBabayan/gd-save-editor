using GD.SaveEditor.Presentation.ViewModels.Characters;
using GD.SaveEditor.Services.GameData.Waypoints;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Presentation.ViewModels.Waypoints;

/// <summary>
/// "Waypoints" tab of the selected character: the rift gates of every campaign (base game, Ashes of Malmouth,
/// Forgotten Gods, Fangs of Asterkarn) per difficulty, which can be unlocked (or locked again) one by one, per campaign
/// or all at once. The changes are written when the character is saved.
/// <para>
/// The gates and their campaigns come from the game's world maps (<see cref="IGameWaypointProvider"/>, filled by the
/// database generator). Ids in the save file that the database does not know are kept as they are.
/// </para>
/// </summary>
public sealed partial class WaypointsViewModel : ViewModelBase
{
    private readonly IReadOnlyList<IReadOnlyList<string>> _original;
    private readonly HashSet<string>[] _unlocked;
    private readonly IReadOnlyList<GameWaypoint> _known;
    private readonly HashSet<string> _knownUids;

    public WaypointsViewModel(CharacterSave character, IGameWaypointProvider waypoints)
    {
        Character = character;
        _known = waypoints.GetWaypoints();
        _knownUids = _known.Select(w => w.Uid).ToHashSet(StringComparer.OrdinalIgnoreCase);

        _original = character.Waypoints?.ByDifficulty
                    ?? Enumerable.Range(0, CharacterWaypoints.DifficultyCount).Select(_ => (IReadOnlyList<string>)[]).ToList();
        _unlocked = _original.Select(list => list.ToHashSet(StringComparer.OrdinalIgnoreCase)).ToArray();

        CanEdit = character.CanRewrite && character.Waypoints is not null && _known.Count > 0;
        StatusMessage = character.Waypoints is null
            ? "The waypoints could not be read from this save file."
            : !character.CanRewrite
                ? $"The waypoints cannot be changed for this character: {character.RewriteError}"
                : _known.Count == 0
                    ? "The game's waypoints are not in the database yet. Set the Grim Dawn folder in Settings and generate the database again: the waypoints are read from the game's Levels.arc files."
                    : null;

        DifficultyOptions =
        [
            new("Normal", GameDifficulty.Normal),
            new("Elite", GameDifficulty.Elite),
            new("Ultimate", GameDifficulty.Ultimate)
        ];
        _selectedDifficulty = DifficultyOptions[0];

        Campaigns = GameWaypointCampaigns.All
            .Select(c => new WaypointCampaignViewModel(this, c, _known.Where(w => w.Campaign == c).ToList()))
            .ToList();
    }

    public CharacterSave Character { get; }

    /// <summary>The waypoints can be changed: the file can be rewritten and the database knows the gates.</summary>
    public bool CanEdit { get; }

    /// <summary>Why the waypoints cannot be changed; <c>null</c> when they can.</summary>
    public string? StatusMessage { get; }

    public bool HasStatusMessage => StatusMessage is not null;

    public IReadOnlyList<ChoiceOption<GameDifficulty>> DifficultyOptions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DifficultyHint), nameof(HasDifficultyHint), nameof(UnknownText), nameof(HasUnknown), nameof(SummaryText))]
    private ChoiceOption<GameDifficulty> _selectedDifficulty;

    public GameDifficulty Difficulty => SelectedDifficulty.Value;

    /// <summary>Base campaign, Ashes of Malmouth, Forgotten Gods, Fangs of Asterkarn.</summary>
    public IReadOnlyList<WaypointCampaignViewModel> Campaigns { get; }

    /// <summary>A note when the selected difficulty is not unlocked for the character yet.</summary>
    public string? DifficultyHint =>
        Character.UnlockedDifficulty is { } unlocked && Difficulty > unlocked
            ? $"{SelectedDifficulty.Label} is not unlocked for this character yet; its waypoints are used once it is."
            : null;

    public bool HasDifficultyHint => DifficultyHint is not null;

    /// <summary>Ids of the selected difficulty that the database does not know (kept as they are).</summary>
    public string? UnknownText
    {
        get
        {
            if (!CanEdit)
                return null;

            var unknown = _unlocked[(int)Difficulty].Count(uid => !_knownUids.Contains(uid));
            return unknown == 0
                ? null
                : $"{unknown} unlocked waypoint{(unknown == 1 ? " is" : "s are")} not in the database (e.g. from a mod); kept as they are.";
        }
    }

    public bool HasUnknown => UnknownText is not null;

    /// <summary>"53 of 72 waypoints unlocked in Normal".</summary>
    public string SummaryText =>
        $"{_known.Count(w => IsUnlocked(w.Uid))} of {_known.Count} waypoints unlocked in {SelectedDifficulty.Label}";

    /// <summary>A waypoint was unlocked or locked in some difficulty.</summary>
    public bool HasChanges
    {
        get
        {
            for (var i = 0; i < _unlocked.Length; i++)
            {
                if (!_unlocked[i].SetEquals(_original[i]))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// The three lists to write, or <c>null</c> without changes: the ids of the file that are still unlocked in their
    /// order, then the newly unlocked ones by campaign and map order.
    /// </summary>
    public CharacterWaypoints? Changes
    {
        get
        {
            if (!HasChanges)
                return null;

            var lists = new List<IReadOnlyList<string>>(_unlocked.Length);
            var mapOrder = _known.OrderBy(w => w.Campaign).ThenBy(w => w.Order).Select(w => w.Uid).ToList();
            for (var i = 0; i < _unlocked.Length; i++)
            {
                var original = _original[i];
                var kept = original.Where(_unlocked[i].Contains).ToList();
                var originalSet = original.ToHashSet(StringComparer.OrdinalIgnoreCase);
                kept.AddRange(mapOrder.Where(uid => _unlocked[i].Contains(uid) && !originalSet.Contains(uid)));
                lists.Add(kept);
            }

            return new CharacterWaypoints(lists);
        }
    }

    internal bool IsUnlocked(string uid) => _unlocked[(int)Difficulty].Contains(uid);

    internal bool WasUnlocked(string uid) =>
        _original[(int)Difficulty].Contains(uid, StringComparer.OrdinalIgnoreCase);

    internal void SetUnlocked(string uid, bool unlocked)
    {
        if (!CanEdit)
            return;

        var set = _unlocked[(int)Difficulty];
        if (unlocked ? set.Add(uid) : set.Remove(uid))
            Refresh();
    }

    /// <summary>Unlocks <paramref name="waypoints"/> in the selected difficulty.</summary>
    internal void Unlock(IEnumerable<GameWaypoint> waypoints)
    {
        if (!CanEdit)
            return;

        var set = _unlocked[(int)Difficulty];
        var changed = false;
        foreach (var waypoint in waypoints)
            changed |= set.Add(waypoint.Uid);
        if (changed)
            Refresh();
    }

    partial void OnSelectedDifficultyChanged(ChoiceOption<GameDifficulty> value) => RefreshItems();

    /// <summary>Unlocks every waypoint of every campaign in the selected difficulty.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void UnlockAllInDifficulty() => Unlock(_known);

    /// <summary>Unlocks every waypoint of every campaign in Normal, Elite and Ultimate.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void UnlockAllEverywhere()
    {
        foreach (var set in _unlocked)
        {
            foreach (var waypoint in _known)
                set.Add(waypoint.Uid);
        }

        Refresh();
    }

    /// <summary>Puts every list back as the save file has it.</summary>
    [RelayCommand]
    private void ResetAll()
    {
        for (var i = 0; i < _unlocked.Length; i++)
        {
            _unlocked[i].Clear();
            _unlocked[i].UnionWith(_original[i]);
        }

        Refresh();
    }

    private void Refresh()
    {
        RefreshItems();
        OnPropertyChanged(nameof(HasChanges));
    }

    private void RefreshItems()
    {
        foreach (var campaign in Campaigns)
            campaign.Refresh();
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(UnknownText));
        OnPropertyChanged(nameof(HasUnknown));
    }
}

/// <summary>The waypoints of one campaign in the selected difficulty.</summary>
public sealed partial class WaypointCampaignViewModel : ViewModelBase
{
    private readonly WaypointsViewModel _owner;

    internal WaypointCampaignViewModel(WaypointsViewModel owner, GameWaypointCampaign campaign, IReadOnlyList<GameWaypoint> waypoints)
    {
        _owner = owner;
        Campaign = campaign;
        Title = GameWaypointCampaigns.Title(campaign);
        Waypoints = waypoints.Select(w => new WaypointItemViewModel(owner, w)).ToList();
    }

    public GameWaypointCampaign Campaign { get; }

    public string Title { get; }

    public IReadOnlyList<WaypointItemViewModel> Waypoints { get; }

    public bool HasWaypoints => Waypoints.Count > 0;

    public bool CanEdit => _owner.CanEdit && HasWaypoints;

    public int UnlockedCount => Waypoints.Count(w => w.IsUnlocked);

    public bool IsComplete => HasWaypoints && UnlockedCount == Waypoints.Count;

    /// <summary>"12 / 30".</summary>
    public string CountText => $"{UnlockedCount} / {Waypoints.Count}";

    /// <summary>Shown instead of the list when the database has no waypoint of this campaign.</summary>
    public string EmptyText =>
        $"No {Title} waypoints in the database. Generate the database with the game folder set; the expansion's Levels.arc is needed.";

    /// <summary>Unlocks every waypoint of this campaign in the selected difficulty.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void UnlockAll() => _owner.Unlock(Waypoints.Select(w => w.Waypoint));

    internal void Refresh()
    {
        foreach (var waypoint in Waypoints)
            waypoint.Refresh();
        OnPropertyChanged(nameof(UnlockedCount));
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(CountText));
    }
}

/// <summary>One waypoint: unlocked or not in the selected difficulty.</summary>
public sealed class WaypointItemViewModel : ViewModelBase
{
    private readonly WaypointsViewModel _owner;

    internal WaypointItemViewModel(WaypointsViewModel owner, GameWaypoint waypoint)
    {
        _owner = owner;
        Waypoint = waypoint;
    }

    public GameWaypoint Waypoint { get; }

    public string Name => Waypoint.Name;

    public string ToolTip => Waypoint.RecordPath;

    public bool CanEdit => _owner.CanEdit;

    public bool IsUnlocked
    {
        get => _owner.IsUnlocked(Waypoint.Uid);
        set => _owner.SetUnlocked(Waypoint.Uid, value);
    }

    /// <summary>Unlocked or locked in the editor, not saved yet.</summary>
    public bool IsChanged => IsUnlocked != _owner.WasUnlocked(Waypoint.Uid);

    internal void Refresh()
    {
        OnPropertyChanged(nameof(IsUnlocked));
        OnPropertyChanged(nameof(IsChanged));
    }
}
