using System.Collections.ObjectModel;
using GD.SaveEditor.Services.Notifications;

namespace GD.SaveEditor.Presentation.Services.Toasts;

/// <summary>
/// Shows toast messages. Toasts are transient: they are not stored in the notification center.
/// Must be used from the UI thread.
/// </summary>
public interface IToastService
{
    /// <summary>Visible toasts, oldest first (the newest is shown at the bottom).</summary>
    ReadOnlyObservableCollection<Toast> Toasts { get; }

    /// <summary>Closes a toast (bound to the × button).</summary>
    IRelayCommand<Toast> DismissCommand { get; }

    /// <summary>Shows a toast that closes by itself after <paramref name="duration"/> (default 5 seconds).</summary>
    Toast Show(string message, NotificationSeverity severity = NotificationSeverity.Info, TimeSpan? duration = null);

    /// <summary>
    /// Shows a toast that stays until <see cref="Dismiss"/> is called (progress, countdown).
    /// </summary>
    Toast ShowPersistent(string message, NotificationSeverity severity = NotificationSeverity.Info, bool isClosable = false);

    void Dismiss(Toast toast);
}
