using DisplaySelector.Core.Logging;
using Microsoft.Toolkit.Uwp.Notifications;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace DisplaySelector.Core.Notifications;

/// <summary>
/// Win11 toast notifications via the Community Toolkit compat layer (unpackaged Win32). Routine toasts
/// share one <see cref="StatusTag"/>/<see cref="Group"/>, so Windows REPLACES the previous one instead
/// of queuing — only the most recent action is ever shown. Falls back to a tray balloon if toasts are
/// unavailable (e.g. no Start-menu shortcut / AUMID registration failed).
/// </summary>
public sealed class ToastNotificationService : INotificationService
{
    private const string StatusTag = "status";
    private const string Group = "displayselector";

    private readonly ILog _log;
    private readonly Action<string, NotificationLevel> _fallback;
    private bool _toastsUnavailable;

    public ToastNotificationService(ILog log, Action<string, NotificationLevel> fallback)
    {
        _log = log;
        _fallback = fallback;
    }

    public void Show(string message, NotificationLevel level = NotificationLevel.Info) =>
        TryToastOrFallback(
            () => ShowToast(StatusTag, builder => builder.AddText(message)),
            message,
            level);

    private void TryToastOrFallback(Action showToast, string fallbackMessage, NotificationLevel level)
    {
        if (!_toastsUnavailable)
        {
            try
            {
                showToast();
                return;
            }
            catch (Exception ex)
            {
                _log.Error("Toast notification failed; falling back to tray balloon for the rest of this session.", ex);
                _toastsUnavailable = true;
            }
        }

        _fallback(fallbackMessage, level);
    }

    // Builds an app-titled toast, applies the caller's content, and shows it under the given tag.
    // Same Tag + Group => Windows replaces the existing toast rather than enqueuing a new one.
    private static void ShowToast(string tag, Action<ToastContentBuilder> addContent)
    {
        var builder = new ToastContentBuilder().AddText(AppIdentity.AppName);
        addContent(builder);

        var xml = new XmlDocument();
        xml.LoadXml(builder.GetToastContent().GetContent());

        var toast = new ToastNotification(xml) { Tag = tag, Group = Group };
        ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
    }
}
