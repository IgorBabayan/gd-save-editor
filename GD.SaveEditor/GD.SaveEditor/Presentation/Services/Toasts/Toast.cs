using GD.SaveEditor.Services.Notifications;

namespace GD.SaveEditor.Presentation.Services.Toasts;

/// <summary>
/// A short-lived message shown in the bottom-right corner of the active window.
/// Text, progress and the action can be changed while the toast is visible (e.g. a countdown).
/// </summary>
public sealed partial class Toast : ObservableObject
{
    internal Toast(string message, NotificationSeverity severity, bool isClosable)
    {
        Message = message;
        Severity = severity;
        IsClosable = isClosable;
    }

    public NotificationSeverity Severity { get; }

    /// <summary>Whether the toast shows a close (×) button.</summary>
    public bool IsClosable { get; }

    [ObservableProperty]
    private string _message = string.Empty;

    /// <summary>Optional second line under the message.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetails))]
    private string? _details;

    public bool HasDetails => !string.IsNullOrWhiteSpace(Details);

    /// <summary>Progress in the range 0..1, or <c>null</c> to hide the progress bar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProgress))]
    [NotifyPropertyChangedFor(nameof(ProgressPercent))]
    private double? _progress;

    public bool HasProgress => Progress is not null;

    public double ProgressPercent => (Progress ?? 0) * 100;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAction))]
    private string? _actionText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAction))]
    private IRelayCommand? _actionCommand;

    public bool HasAction => ActionCommand is not null && !string.IsNullOrWhiteSpace(ActionText);

    /// <summary>Shows a link-style button (e.g. "Cancel") on the toast.</summary>
    public void SetAction(string text, Action action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(action);

        ActionText = text;
        ActionCommand = new RelayCommand(action);
    }
}
