using System.Text.Json.Serialization;

namespace GD.SaveEditor.Services.Notifications;

/// <summary>A single entry of the notification center.</summary>
public sealed partial class Notification : ObservableObject
{
    [JsonConstructor]
    public Notification(Guid id, string title, string? message, NotificationSeverity severity, DateTimeOffset createdAt, bool isRead)
    {
        Id = id;
        Title = title;
        Message = message;
        Severity = severity;
        CreatedAt = createdAt;
        IsRead = isRead;
    }

    public Notification(string title, string? message, NotificationSeverity severity)
        : this(Guid.NewGuid(), title, message, severity, DateTimeOffset.Now, isRead: false)
    {
    }

    public Guid Id { get; }

    public string Title { get; }

    public string? Message { get; }

    [JsonIgnore]
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    public NotificationSeverity Severity { get; }

    /// <summary>When the notification was raised (or last repeated, see <see cref="INotificationService.Notify"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CreatedAtLocal))]
    private DateTimeOffset _createdAt;

    [JsonIgnore]
    public DateTime CreatedAtLocal => CreatedAt.LocalDateTime;

    [ObservableProperty]
    private bool _isRead;
}
