using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Threading;

namespace GD.SaveEditor.Services.Notifications;

/// <summary>
/// Notification history persisted as JSON in <c>%APPDATA%\GD.SaveEditor\notifications.json</c>,
/// so earlier notifications are still there after a restart.
/// </summary>
public sealed class JsonNotificationService : INotificationService
{
    /// <summary>Oldest notifications are dropped beyond this count.</summary>
    private const int MaxNotifications = 200;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GD.SaveEditor",
        "notifications.json");

    private readonly ObservableCollection<Notification> _notifications = [];
    private readonly Dispatcher _dispatcher;

    public JsonNotificationService()
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        Notifications = new ReadOnlyObservableCollection<Notification>(_notifications);

        foreach (var notification in Load().OrderByDescending(n => n.CreatedAt).Take(MaxNotifications))
            Add(notification, _notifications.Count);
    }

    public ReadOnlyObservableCollection<Notification> Notifications { get; }

    public int UnreadCount => _notifications.Count(n => !n.IsRead);

    public event EventHandler? UnreadCountChanged;

    public void Notify(string title, string? message = null, NotificationSeverity severity = NotificationSeverity.Info)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.InvokeAsync(() => Notify(title, message, severity));
            return;
        }

        var existing = _notifications.FirstOrDefault(n =>
            !n.IsRead
            && n.Severity == severity
            && string.Equals(n.Title, title, StringComparison.Ordinal)
            && string.Equals(n.Message, message, StringComparison.Ordinal));

        if (existing is not null)
        {
            // Same problem reported again (e.g. on every reload): bump it instead of piling up duplicates.
            existing.CreatedAt = DateTimeOffset.Now;
            var index = _notifications.IndexOf(existing);
            if (index > 0)
                _notifications.Move(index, 0);
        }
        else
        {
            Add(new Notification(title, message, severity), 0);

            while (_notifications.Count > MaxNotifications)
                RemoveAt(_notifications.Count - 1);
        }

        Changed();
    }

    public void MarkAsRead(Notification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (notification.IsRead)
            return;

        // IsRead change triggers Changed() through OnNotificationPropertyChanged.
        notification.IsRead = true;
    }

    public void MarkAllAsRead()
    {
        var unread = _notifications.Where(n => !n.IsRead).ToList();
        if (unread.Count == 0)
            return;

        _suppressChanged = true;
        try
        {
            foreach (var notification in unread)
                notification.IsRead = true;
        }
        finally
        {
            _suppressChanged = false;
        }

        Changed();
    }

    public void Remove(Notification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var index = _notifications.IndexOf(notification);
        if (index < 0)
            return;

        RemoveAt(index);
        Changed();
    }

    public void Clear()
    {
        if (_notifications.Count == 0)
            return;

        foreach (var notification in _notifications)
            notification.PropertyChanged -= OnNotificationPropertyChanged;

        _notifications.Clear();
        Changed();
    }

    #region Collection helpers

    private bool _suppressChanged;

    private void Add(Notification notification, int index)
    {
        notification.PropertyChanged += OnNotificationPropertyChanged;
        _notifications.Insert(index, notification);
    }

    private void RemoveAt(int index)
    {
        _notifications[index].PropertyChanged -= OnNotificationPropertyChanged;
        _notifications.RemoveAt(index);
    }

    private void OnNotificationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Notification.IsRead) && !_suppressChanged)
            Changed();
    }

    private void Changed()
    {
        Persist();
        UnreadCountChanged?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Persistence

    private void Persist()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

            // Write to a temp file first so a crash mid-write never leaves a corrupted file.
            var tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(_notifications, SerializerOptions));
            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing the history is not worth crashing over; the in-memory list stays intact.
            System.Diagnostics.Debug.WriteLine($"Saving notifications failed: {ex.Message}");
        }
    }

    private List<Notification> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return [];

            return JsonSerializer.Deserialize<List<Notification>>(File.ReadAllText(_filePath), SerializerOptions)?
                       .Where(n => !string.IsNullOrWhiteSpace(n.Title))
                       .ToList()
                   ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable or corrupted history must not prevent the app from starting.
            return [];
        }
    }

    #endregion
}
