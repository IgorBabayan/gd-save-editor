using System.Globalization;

namespace GD.SaveEditor.Presentation.ViewModels.Characters;

/// <summary>
/// A whole-number field of the character panel (unspent points, attributes, Crucible tributes): the value of the save
/// file, the text typed and whether it is a valid change.
/// </summary>
public sealed partial class NumberFieldViewModel : ViewModelBase
{
    /// <param name="original">Value of the save file; <c>null</c> when that part of the file could not be read.</param>
    /// <param name="readOnlyReason">Why the value cannot be changed; <c>null</c> when it can.</param>
    public NumberFieldViewModel(string label, long? original, int maximum, string? readOnlyReason, string? hint = null)
    {
        Label = label;
        Original = original;
        Maximum = maximum;
        CanEdit = original is not null && readOnlyReason is null;
        ToolTip = CanEdit
            ? $"{hint ?? label}. Type a value from 0 to {Format(maximum)} (Up/Down change it by one, Esc undoes), then save the character."
            : $"{label} cannot be changed for this character: {readOnlyReason ?? "it could not be read from the save file."}";
        Text = original is { } value ? value.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    public string Label { get; }

    /// <summary>Value of the save file; <c>null</c> when it could not be read (the field is then hidden).</summary>
    public long? Original { get; }

    public bool IsAvailable => Original is not null;

    /// <summary>Largest value that can be written (the Up arrow key stops there as well).</summary>
    public int Maximum { get; }

    public bool CanEdit { get; }

    public bool IsReadOnly => !CanEdit;

    public string ToolTip { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Error), nameof(HasError), nameof(IsChanged), nameof(ChangedValue))]
    private string _text = string.Empty;

    /// <summary>Why <see cref="Text"/> is not a valid value; <c>null</c> when it is.</summary>
    public string? Error => !CanEdit || TryParse(Text, out _)
        ? null
        : $"{Label}: enter a whole number from 0 to {Format(Maximum)}.";

    public bool HasError => Error is not null;

    /// <summary>The field holds a valid value that differs from the save file.</summary>
    public bool IsChanged => ChangedValue is not null;

    /// <summary>The value to write; <c>null</c> when it is unchanged or not valid.</summary>
    public int? ChangedValue => CanEdit && TryParse(Text, out var value) && value != Original ? value : null;

    /// <summary>Puts the value of the save file back.</summary>
    [RelayCommand]
    public void Reset()
    {
        if (Original is { } value)
            Text = value.ToString(CultureInfo.InvariantCulture);
    }

    private bool TryParse(string? text, out int value)
    {
        value = 0;
        var trimmed = text?.Trim();
        return !string.IsNullOrEmpty(trimmed)
               && trimmed.All(char.IsAsciiDigit)
               && int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out value)
               && value <= Maximum;
    }

    private static string Format(long value) => value.ToString("N0", CultureInfo.CurrentCulture);
}
