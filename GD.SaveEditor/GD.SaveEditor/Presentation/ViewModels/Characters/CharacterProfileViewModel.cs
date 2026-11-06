using System.ComponentModel;
using System.Globalization;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Presentation.ViewModels.Characters;

/// <summary>An attribute or counter shown beside the equipment, e.g. "Base health 1,250".</summary>
public sealed record CharacterStatLine(string Label, string Value);

/// <summary>One choice of a pill selector (sex, difficulty).</summary>
public sealed record ChoiceOption<T>(string Label, T Value);

/// <summary>
/// Character panel of the "Character" tab, beside the equipment: name, sex, unlocked difficulty, Crucible tributes,
/// attributes and unspent points, which can be changed and are written when the character is saved; and the base
/// health and energy, experience and devotion earned, shown as they are in the file.
/// </summary>
public sealed partial class CharacterProfileViewModel : ViewModelBase
{
    public CharacterProfileViewModel(CharacterSave character)
    {
        Character = character;
        var stats = character.Stats;

        // Every value is written by encrypting the whole file again, so all of them need a file the editor can rewrite.
        var readOnlyReason = character.CanRewrite ? null : character.RewriteError;
        CanEditHeader = readOnlyReason is null;
        CanEditDifficulty = readOnlyReason is null && character.UnlockedDifficulty is not null;

        SexOptions = [new("Male", true), new("Female", false)];
        DifficultyOptions =
        [
            new("Normal", GameDifficulty.Normal),
            new("Elite", GameDifficulty.Elite),
            new("Ultimate", GameDifficulty.Ultimate)
        ];

        CrucibleTributes = new NumberFieldViewModel(
            "Crucible tributes", character.CrucibleTributes, CharacterLimits.MaxTributes, readOnlyReason,
            "Tributes spent on blessings in the Crucible");
        Physique = new NumberFieldViewModel("Physique", Round(stats?.Physique), CharacterLimits.MaxAttribute, readOnlyReason,
            $"Physique. Base health moves with it ({CharacterLimits.HealthPerPhysique} per point)");
        Cunning = new NumberFieldViewModel("Cunning", Round(stats?.Cunning), CharacterLimits.MaxAttribute, readOnlyReason);
        Spirit = new NumberFieldViewModel("Spirit", Round(stats?.Spirit), CharacterLimits.MaxAttribute, readOnlyReason,
            $"Spirit. Base energy moves with it ({CharacterLimits.EnergyPerSpirit} per point)");
        AttributePoints = new NumberFieldViewModel("Attribute points", stats?.AttributePoints, CharacterLimits.MaxPoints, readOnlyReason,
            "Unspent attribute points");
        SkillPoints = new NumberFieldViewModel("Skill points", stats?.SkillPoints, CharacterLimits.MaxPoints, readOnlyReason,
            "Unspent skill points");
        DevotionPoints = new NumberFieldViewModel(
            "Devotion points", stats?.DevotionPoints, CharacterLimits.MaxDevotionPoints, readOnlyReason,
            $"Unspent devotion points (at most {CharacterLimits.MaxDevotionPoints})");

        Attributes = [Physique, Cunning, Spirit];
        Points = [AttributePoints, SkillPoints, DevotionPoints];
        foreach (var field in Attributes.Concat(Points).Append(CrucibleTributes))
            field.PropertyChanged += OnFieldChanged;

        ReadOnlyStats = BuildReadOnlyStats(stats);
        ResetAll();
    }

    /// <summary>Longest name the name field takes.</summary>
    public const int MaxNameLength = CharacterLimits.MaxNameLength;

    public CharacterSave Character { get; }

    /// <summary>Name and sex can be changed: the editor can write the save file back.</summary>
    public bool CanEditHeader { get; }

    public bool IsHeaderReadOnly => !CanEditHeader;

    /// <summary>The unlocked difficulty was read and can be changed.</summary>
    public bool CanEditDifficulty { get; }

    public bool HasDifficulty => Character.UnlockedDifficulty is not null;

    /// <summary>Why nothing here can be changed; <c>null</c> when it can.</summary>
    public string? EditHint => CanEditHeader ? null : $"The character cannot be changed: {Character.RewriteError}";

    public bool HasEditHint => EditHint is not null;

    #region Name

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameError), nameof(HasNameError), nameof(IsNameChanged), nameof(HasChanges), nameof(HasErrors))]
    private string _nameText = string.Empty;

    public string? NameError => CanEditHeader ? CharacterLimits.ValidateName(NameText) : null;

    public bool HasNameError => NameError is not null;

    public bool IsNameChanged => ChangedName is not null;

    /// <summary>The name to write; <c>null</c> when it is unchanged or not valid.</summary>
    private string? ChangedName =>
        CanEditHeader && NameError is null && !string.Equals(NameText, Character.CharacterName, StringComparison.Ordinal)
            ? NameText
            : null;

    public string NameToolTip => CanEditHeader
        ? $"Name of the character (at most {CharacterLimits.MaxNameLength} characters). Esc undoes the change."
        : $"The name cannot be changed for this character: {Character.RewriteError}";

    [RelayCommand]
    private void ResetName() => NameText = Character.CharacterName;

    #endregion

    #region Sex, difficulty

    public IReadOnlyList<ChoiceOption<bool>> SexOptions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSexChanged), nameof(HasChanges))]
    private ChoiceOption<bool>? _selectedSex;

    public bool IsSexChanged => CanEditHeader && SelectedSex is { } sex && sex.Value != Character.IsMale;

    public string SexToolTip => CanEditHeader
        ? "Sex of the character model."
        : $"The sex cannot be changed for this character: {Character.RewriteError}";

    public IReadOnlyList<ChoiceOption<GameDifficulty>> DifficultyOptions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDifficultyChanged), nameof(HasChanges), nameof(DifficultyHint), nameof(HasDifficultyHint))]
    private ChoiceOption<GameDifficulty>? _selectedDifficulty;

    public bool IsDifficultyChanged =>
        CanEditDifficulty && SelectedDifficulty is { } difficulty && difficulty.Value != Character.UnlockedDifficulty;

    /// <summary>What lowering the unlocked difficulty does; <c>null</c> otherwise.</summary>
    public string? DifficultyHint =>
        IsDifficultyChanged && SelectedDifficulty!.Value < Character.UnlockedDifficulty
            ? $"The character will start in {SelectedDifficulty.Label} if it last played a higher difficulty."
            : null;

    public bool HasDifficultyHint => DifficultyHint is not null;

    public string DifficultyToolTip => CanEditDifficulty
        ? "Highest difficulty the character may choose when entering the game."
        : $"The unlocked difficulty cannot be changed for this character: {Character.RewriteError ?? "it could not be read from the save file."}";

    #endregion

    #region Numbers

    public NumberFieldViewModel CrucibleTributes { get; }

    public NumberFieldViewModel Physique { get; }

    public NumberFieldViewModel Cunning { get; }

    public NumberFieldViewModel Spirit { get; }

    public NumberFieldViewModel AttributePoints { get; }

    public NumberFieldViewModel SkillPoints { get; }

    public NumberFieldViewModel DevotionPoints { get; }

    /// <summary>Physique, cunning, spirit.</summary>
    public IReadOnlyList<NumberFieldViewModel> Attributes { get; }

    /// <summary>Unspent attribute, skill and devotion points.</summary>
    public IReadOnlyList<NumberFieldViewModel> Points { get; }

    /// <summary>The attributes and points were read from the save file.</summary>
    public bool HasStats => Character.Stats is not null;

    /// <summary>Base health and energy, experience, devotion earned: shown as the save file has them.</summary>
    public IReadOnlyList<CharacterStatLine> ReadOnlyStats { get; }

    #endregion

    /// <summary>A field holds a valid value that differs from the save file.</summary>
    public bool HasChanges =>
        IsNameChanged || IsSexChanged || IsDifficultyChanged || NumberFields.Any(f => f.IsChanged);

    /// <summary>A field holds a value that cannot be written (it is left out when saving).</summary>
    public bool HasErrors => HasNameError || NumberFields.Any(f => f.HasError);

    /// <summary>What to write into the save file; <c>null</c> when nothing changed.</summary>
    public ProfileChanges? Changes
    {
        get
        {
            if (!HasChanges)
                return null;

            return new ProfileChanges(
                Name: ChangedName,
                IsMale: IsSexChanged ? SelectedSex!.Value : null,
                UnlockedDifficulty: IsDifficultyChanged ? SelectedDifficulty!.Value : null,
                CrucibleTributes: CrucibleTributes.ChangedValue is { } tributes ? (uint)tributes : null,
                AttributePoints: AttributePoints.ChangedValue,
                SkillPoints: SkillPoints.ChangedValue,
                DevotionPoints: DevotionPoints.ChangedValue,
                Physique: Physique.ChangedValue,
                Cunning: Cunning.ChangedValue,
                Spirit: Spirit.ChangedValue);
        }
    }

    /// <summary>Puts every value of the save file back.</summary>
    [RelayCommand]
    private void ResetAll()
    {
        NameText = Character.CharacterName;
        SelectedSex = SexOptions.First(o => o.Value == Character.IsMale);
        SelectedDifficulty = DifficultyOptions.FirstOrDefault(o => o.Value == Character.UnlockedDifficulty);
        foreach (var field in NumberFields)
            field.Reset();
    }

    private IEnumerable<NumberFieldViewModel> NumberFields => Attributes.Concat(Points).Append(CrucibleTributes);

    private void OnFieldChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NumberFieldViewModel.Text))
        {
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(HasErrors));
        }
    }

    private static long? Round(float? value) => value is { } v ? (long)Math.Round(v) : null;

    private static IReadOnlyList<CharacterStatLine> BuildReadOnlyStats(CharacterStats? stats)
    {
        if (stats is null)
            return [];

        static string Number(double value) => value.ToString("N0", CultureInfo.CurrentCulture);

        return
        [
            new("Base health", Number(stats.Health)),
            new("Base energy", Number(stats.Energy)),
            new("Experience", Number(stats.Experience)),
            new("Devotion earned", Number(stats.TotalDevotion))
        ];
    }
}
