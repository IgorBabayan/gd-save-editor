using System.Collections.ObjectModel;
using System.Windows.Threading;
using GD.SaveEditor.Services.Notifications;

namespace GD.SaveEditor.Presentation.Services.Toasts;

public sealed class ToastService : IToastService
{
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(5);

    /// <summary>Older auto-closing toasts are dropped beyond this count.</summary>
    private const int MaxVisible = 4;

    private readonly ObservableCollection<Toast> _toasts = [];
    private readonly Dictionary<Toast, DispatcherTimer> _timers = new();

    public ToastService()
    {
        Toasts = new ReadOnlyObservableCollection<Toast>(_toasts);
        DismissCommand = new RelayCommand<Toast>(toast =>
        {
            if (toast is not null)
                Dismiss(toast);
        });
    }

    public ReadOnlyObservableCollection<Toast> Toasts { get; }

    public IRelayCommand<Toast> DismissCommand { get; }

    public Toast Show(string message, NotificationSeverity severity = NotificationSeverity.Info, TimeSpan? duration = null)
    {
        var toast = Add(message, severity, isClosable: true);

        var timer = new DispatcherTimer { Interval = duration ?? DefaultDuration };
        timer.Tick += (_, _) => Dismiss(toast);
        _timers[toast] = timer;
        timer.Start();

        return toast;
    }

    public Toast ShowPersistent(string message, NotificationSeverity severity = NotificationSeverity.Info, bool isClosable = false) =>
        Add(message, severity, isClosable);

    public void Dismiss(Toast toast)
    {
        ArgumentNullException.ThrowIfNull(toast);

        if (_timers.Remove(toast, out var timer))
            timer.Stop();

        _toasts.Remove(toast);
    }

    private Toast Add(string message, NotificationSeverity severity, bool isClosable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var toast = new Toast(message, severity, isClosable);
        _toasts.Add(toast);

        // Keep the stack short: drop the oldest auto-closing toasts first, never progress/countdown ones.
        while (_toasts.Count > MaxVisible)
        {
            var oldest = _toasts.FirstOrDefault(t => _timers.ContainsKey(t));
            if (oldest is null)
                break;
            Dismiss(oldest);
        }

        return toast;
    }
}
