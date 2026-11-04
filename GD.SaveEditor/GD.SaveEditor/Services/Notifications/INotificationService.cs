using System.Collections.ObjectModel;

namespace GD.SaveEditor.Services.Notifications;

/// <summary>
/// Keeps the notification history shown in the notification center.
/// All members must be used from the UI thread except <see cref="Notify"/>, which may be called from any thread.
/// </summary>
public interface INotificationService
{
    /// <summary>All notifications, newest first.</summary>
    ReadOnlyObservableCollection<Notification> Notifications { get; }

    /// <summary>Number of notifications that are not read yet.</summary>
    int UnreadCount { get; }

    /// <summary>Raised whenever <see cref="UnreadCount"/> may have changed.</summary>
    event EventHandler? UnreadCountChanged;

    /// <summary>
    /// Adds a notification. If an identical notification is still unread, it is moved to the top
    /// with a fresh timestamp instead of being duplicated.
    /// </summary>
    void Notify(string title, string? message = null, NotificationSeverity severity = NotificationSeverity.Info);

    void MarkAsRead(Notification notification);

    void MarkAllAsRead();

    void Remove(Notification notification);

    void Clear();
}
