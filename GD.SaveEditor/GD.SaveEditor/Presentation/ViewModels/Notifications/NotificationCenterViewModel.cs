using System.Collections.ObjectModel;
using System.Collections.Specialized;
using GD.SaveEditor.Services.Notifications;

namespace GD.SaveEditor.Presentation.ViewModels.Notifications;

/// <summary>Bell button in the title bar and the popup with the notification history.</summary>
public sealed partial class NotificationCenterViewModel : ViewModelBase
{
    private readonly INotificationService _notificationService;

    public NotificationCenterViewModel(INotificationService notificationService)
    {
        _notificationService = notificationService;

        _notificationService.UnreadCountChanged += (_, _) => Refresh();
        ((INotifyCollectionChanged)Notifications).CollectionChanged += (_, _) => Refresh();
    }

    /// <summary>All notifications, newest first.</summary>
    public ReadOnlyObservableCollection<Notification> Notifications => _notificationService.Notifications;

    /// <summary>Whether the popup is shown; bound two-way to the bell toggle button and the popup.</summary>
    [ObservableProperty]
    private bool _isOpen;

    public int UnreadCount => _notificationService.UnreadCount;

    /// <summary>Drives the bell icon: filled when something is unread, outlined otherwise.</summary>
    public bool HasUnread => UnreadCount > 0;

    public bool HasNotifications => Notifications.Count > 0;

    public bool IsEmpty => !HasNotifications;

    public string BellToolTip => UnreadCount switch
    {
        0 => "Notifications",
        1 => "Notifications (1 unread)",
        var count => $"Notifications ({count} unread)"
    };

    [RelayCommand(CanExecute = nameof(HasUnread))]
    private void MarkAllAsRead() => _notificationService.MarkAllAsRead();

    [RelayCommand(CanExecute = nameof(HasNotifications))]
    private void ClearAll() => _notificationService.Clear();

    [RelayCommand]
    private void MarkAsRead(Notification? notification)
    {
        if (notification is not null)
            _notificationService.MarkAsRead(notification);
    }

    [RelayCommand]
    private void Remove(Notification? notification)
    {
        if (notification is not null)
            _notificationService.Remove(notification);
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(UnreadCount));
        OnPropertyChanged(nameof(HasUnread));
        OnPropertyChanged(nameof(HasNotifications));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(BellToolTip));
        MarkAllAsReadCommand.NotifyCanExecuteChanged();
        ClearAllCommand.NotifyCanExecuteChanged();
    }
}
