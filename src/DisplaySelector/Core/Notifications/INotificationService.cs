namespace DisplaySelector.Core.Notifications;

public enum NotificationLevel
{
    Info,
    Warning,
}

/// <summary>User-facing notifications. Implementations replace the previous notification so a rapid
/// burst of actions collapses to the most recent (no Win11 notification-queue lag).</summary>
public interface INotificationService
{
    void Show(string message, NotificationLevel level = NotificationLevel.Info);

    /// <summary>Show a notification with a button that opens <paramref name="url"/> (falls back to text + URL).</summary>
    void ShowWithLink(string message, string linkLabel, string url, NotificationLevel level = NotificationLevel.Info);
}
