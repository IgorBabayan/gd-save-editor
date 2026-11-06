using System.Globalization;
using GD.SaveEditor.Services.GameData.Database;
using GD.SaveEditor.Services.Saves;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Presentation.ViewModels.Factions;

/// <summary>
/// "Factions" tab of the selected character: the reputation with every faction the game names, which can be typed,
/// set to a rank (Nemesis ... Revered) or set for every ally or enemy at once. The changes are written when the
/// character is saved.
/// <para>
/// The save file keeps the factions in a list the game knows by position: the first ones have names of their own
/// (<see cref="CharacterFactions.BuiltInName"/>), the others are named by the game texts
/// (<see cref="CharacterFactions.TextTag"/>, from <c>Text_EN.arc</c> via the database). Positions without a name are
/// not used for the player's reputation and are not shown.
/// </para>
/// </summary>
public sealed partial class FactionsViewModel : ViewModelBase
{
    public FactionsViewModel(CharacterSave character, IGameTextProvider texts)
    {
        Character = character;
        var standings = character.Factions?.Factions ?? [];

        var items = new List<FactionItemViewModel>();
        var hasTextNames = false;
        foreach (var standing in standings)
        {
            var name = CharacterFactions.BuiltInName(standing.Index);
            if (name is null && CharacterFactions.TextTag(standing.Index) is { } tag)
            {
                name = UsableName(texts.GetText(tag));
                hasTextNames |= name is not null;
            }

            if (name is not null)
                items.Add(new FactionItemViewModel(this, standing, name));
        }

        CanEdit = character.CanRewrite && character.Factions is not null;
        StatusMessage = character.Factions is null
            ? "The factions could not be read from this save file."
            : !character.CanRewrite
                ? $"The factions cannot be changed for this character: {character.RewriteError}"
                : !hasTextNames
                    ? "Most faction names come from the game texts, which are not in the database yet. Generate the database (with the Grim Dawn folder set in Settings) to see every faction."
                    : null;

        // Allies first, then enemies, as the save file stands; by name within each.
        Allies = new FactionGroupViewModel(this, "Allies", isHostile: false,
            items.Where(f => !f.StartsHostile).OrderBy(f => f.Name, StringComparer.CurrentCulture).ToList());
        Enemies = new FactionGroupViewModel(this, "Enemies", isHostile: true,
            items.Where(f => f.StartsHostile).OrderBy(f => f.Name, StringComparer.CurrentCulture).ToList());
        All = items;
    }

    public CharacterSave Character { get; }

    /// <summary>The reputations can be changed: the file can be rewritten and the faction block was read.</summary>
    public bool CanEdit { get; }

    /// <summary>Why the factions cannot be changed or are not all shown; <c>null</c> when everything is fine.</summary>
    public string? StatusMessage { get; }

    public bool HasStatusMessage => StatusMessage is not null;

    /// <summary>Factions with a friendly (or neutral) standing in the save file.</summary>
    public FactionGroupViewModel Allies { get; }

    /// <summary>Factions with infamy in the save file.</summary>
    public FactionGroupViewModel Enemies { get; }

    public IReadOnlyList<FactionItemViewModel> All { get; }

    public bool HasFactions => All.Count > 0;

    /// <summary>"7 of 14 allies Revered · 2 changed".</summary>
    public string SummaryText
    {
        get
        {
            var revered = Allies.Factions.Count(f => f.CurrentValue is { } v && v >= CharacterFactions.Revered.Value);
            var changed = All.Count(f => f.IsChanged);
            var text = $"{revered} of {Allies.Factions.Count} allies Revered";
            return changed == 0 ? text : $"{text} · {changed} changed";
        }
    }

    /// <summary>A reputation was changed (and is valid).</summary>
    public bool HasChanges => All.Any(f => f.IsChanged);

    /// <summary>A reputation box holds text that is not a valid value.</summary>
    public bool HasErrors => All.Any(f => f.HasError);

    /// <summary>The reputations to write, or <c>null</c> without changes.</summary>
    public IReadOnlyList<FactionChange>? Changes
    {
        get
        {
            var changes = All
                .Where(f => f.ChangedValue is not null)
                .Select(f => new FactionChange(f.Index, f.ChangedValue!.Value))
                .ToList();
            return changes.Count == 0 ? null : changes;
        }
    }

    /// <summary>Every ally Revered and every enemy Nemesis.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void MaxAll()
    {
        Allies.SetAll(CharacterFactions.Revered);
        Enemies.SetAll(CharacterFactions.Nemesis);
    }

    /// <summary>Puts every reputation back as the save file has it.</summary>
    [RelayCommand]
    private void ResetAll()
    {
        foreach (var faction in All)
            faction.Reset();
    }

    internal void OnFactionChanged()
    {
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(SummaryText));
        Allies.Refresh();
        Enemies.Refresh();
    }

    /// <summary>The game text, unless it is missing or one of the "User12" placeholders of unused positions.</summary>
    private static string? UsableName(string? text)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        return trimmed.StartsWith("User", StringComparison.OrdinalIgnoreCase) ? null : trimmed;
    }
}

/// <summary>The allies or the enemies.</summary>
public sealed partial class FactionGroupViewModel : ViewModelBase
{
    private readonly FactionsViewModel _owner;

    internal FactionGroupViewModel(FactionsViewModel owner, string title, bool isHostile, IReadOnlyList<FactionItemViewModel> factions)
    {
        _owner = owner;
        Title = title;
        IsHostile = isHostile;
        Factions = factions;
    }

    public string Title { get; }

    public bool IsHostile { get; }

    public IReadOnlyList<FactionItemViewModel> Factions { get; }

    public bool HasFactions => Factions.Count > 0;

    public bool CanEdit => _owner.CanEdit && HasFactions;

    /// <summary>"Revered" for the allies, "Nemesis" for the enemies.</summary>
    public string MaxTierName => IsHostile ? CharacterFactions.Nemesis.Name : CharacterFactions.Revered.Name;

    /// <summary>"5 / 9 Revered".</summary>
    public string CountText
    {
        get
        {
            var max = IsHostile ? CharacterFactions.Nemesis : CharacterFactions.Revered;
            var reached = Factions.Count(f => f.CurrentValue is { } v && (IsHostile ? v <= max.Value : v >= max.Value));
            return $"{reached} / {Factions.Count} {max.Name}";
        }
    }

    /// <summary>Allies: Revered; enemies: Nemesis.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void SetAllToMax() => SetAll(IsHostile ? CharacterFactions.Nemesis : CharacterFactions.Revered);

    /// <summary>Back to Tolerated (0): allies lose their reputation, enemies their infamy.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void SetAllToNeutral() => SetAll(CharacterFactions.Tolerated);

    internal void SetAll(FactionTier tier)
    {
        if (!_owner.CanEdit)
            return;
        foreach (var faction in Factions)
            faction.SetTier(tier);
    }

    internal void Refresh() => OnPropertyChanged(nameof(CountText));
}

/// <summary>One faction: its reputation as typed, its rank, and whether that differs from the save file.</summary>
public sealed partial class FactionItemViewModel : ViewModelBase
{
    private readonly FactionsViewModel _owner;

    internal FactionItemViewModel(FactionsViewModel owner, FactionStanding standing, string name)
    {
        _owner = owner;
        Standing = standing;
        Name = name;
        // The file keeps a float; the box shows it rounded, and only a typed change is written.
        Original = float.IsFinite(standing.Value)
            ? (int)Math.Round(Math.Clamp(standing.Value, CharacterFactions.MinValue, CharacterFactions.MaxValue))
            : 0;
        _text = Original.ToString(CultureInfo.InvariantCulture);
    }

    public FactionStanding Standing { get; }

    public int Index => Standing.Index;

    public string Name { get; }

    /// <summary>Value of the save file, rounded.</summary>
    public int Original { get; }

    /// <summary>The faction has infamy in the save file (listed with the enemies).</summary>
    public bool StartsHostile => Standing.Value < 0;

    /// <summary>The character has not met the faction yet; a change unlocks it.</summary>
    public bool IsLocked => !Standing.Unlocked;

    public bool CanEdit => _owner.CanEdit;

    public bool IsReadOnly => !CanEdit;

    public int Minimum => CharacterFactions.MinValue;

    public int Maximum => CharacterFactions.MaxValue;

    public IReadOnlyList<FactionTier> Tiers => CharacterFactions.Tiers;

    public string ToolTip => CanEdit
        ? $"Reputation with {Name}: from {CharacterFactions.MinValue:N0} to {CharacterFactions.MaxValue:N0}; negative values are infamy. "
          + "Up/Down change it by one (Shift: by 100), Esc undoes."
          + (IsLocked ? " Not met yet: a change also unlocks it." : string.Empty)
        : $"{Name}: the reputation cannot be changed for this character.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentValue), nameof(Error), nameof(HasError), nameof(IsChanged), nameof(ChangedValue),
        nameof(Tier), nameof(TierName), nameof(IsHostileNow))]
    private string _text;

    /// <summary>The value typed, when it is valid.</summary>
    public int? CurrentValue => TryParse(Text, out var value) ? value : null;

    /// <summary>Rank of the value typed (of the save file while the text is not valid).</summary>
    public FactionTier Tier => CharacterFactions.TierOf(CurrentValue ?? Original);

    public string TierName => Tier.Name;

    public bool IsHostileNow => Tier.IsHostile;

    public string? Error => !CanEdit || CurrentValue is not null
        ? null
        : $"{Name}: enter a whole number from {CharacterFactions.MinValue:N0} to {CharacterFactions.MaxValue:N0}.";

    public bool HasError => Error is not null;

    public bool IsChanged => ChangedValue is not null;

    /// <summary>The value to write; <c>null</c> when it is unchanged or not valid.</summary>
    public int? ChangedValue => CanEdit && CurrentValue is { } value && value != Original ? value : null;

    /// <summary>Sets the reputation to where <paramref name="tier"/> starts.</summary>
    [RelayCommand]
    public void SetTier(FactionTier? tier)
    {
        if (tier is null || !CanEdit)
            return;
        Text = tier.Value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Puts the value of the save file back.</summary>
    [RelayCommand]
    public void Reset() => Text = Original.ToString(CultureInfo.InvariantCulture);

    partial void OnTextChanged(string value) => _owner.OnFactionChanged();

    private static bool TryParse(string? text, out int value)
    {
        value = 0;
        var trimmed = text?.Trim();
        return !string.IsNullOrEmpty(trimmed)
               && int.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value)
               && value is >= CharacterFactions.MinValue and <= CharacterFactions.MaxValue;
    }
}
