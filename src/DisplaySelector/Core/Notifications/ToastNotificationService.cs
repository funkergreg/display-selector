using DisplaySelector.Core.Logging;
using Microsoft.Toolkit.Uwp.Notifications;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace DisplaySelector.Core.Notifications;

/// <summary>
/// Win11 toast notifications via the Community Toolkit compat layer (unpackaged Win32). Every toast
/// carries the same <see cref="Tag"/>/<see cref="Group"/>, so Windows REPLACES the previous one
/// instead of queuing — only the most recent action is ever shown. Falls back to a tray balloon if
/// toasts are unavailable (e.g. no Start-menu shortcut / AUMID registration failed).
/// </summary>
public sealed class ToastNotificationService : INotificationService
{
    private const string Tag = "status";
    private const string Group = "displayselector";

    private readonly ILog _log;
    private readonly Action<string, NotificationLevel> _fallback;
    private bool _toastsUnavailable;

    public ToastNotificationService(ILog log, Action<string, NotificationLevel> fallback)
    {
        _log = log;
        _fallback = fallback;
    }

    public void Show(string message, NotificationLevel level = NotificationLevel.Info)
    {
        if (!_toastsUnavailable)
        {
            try
            {
                ShowToast(message);
                return;
            }
            catch (Exception ex)
            {
                _log.Error("Toast notification failed; falling back to tray balloon for the rest of this session.", ex);
                _toastsUnavailable = true;
            }
        }

        _fallback(message, level);
    }

    public void ShowWithLink(string message, string linkLabel, string url, NotificationLevel level = NotificationLevel.Info)
    {
        if (!_toastsUnavailable)
        {
            try
            {
                ShowToastWithLink(message, linkLabel, url);
                return;
            }
            catch (Exception ex)
            {
                _log.Error("Toast notification failed; falling back to tray balloon for the rest of this session.", ex);
                _toastsUnavailable = true;
            }
        }

        _fallback($"{message}  {url}", level);
    }

    private static void ShowToast(string message)
    {
        var content = new ToastContentBuilder()
            .AddText(AppIdentity.AppName)
            .AddText(message)
            .GetToastContent();

        var xml = new XmlDocument();
        xml.LoadXml(content.GetContent());

        // Same Tag + Group => Windows replaces the existing toast rather than enqueuing a new one.
        var toast = new ToastNotification(xml) { Tag = Tag, Group = Group };
        ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
    }

    private static void ShowToastWithLink(string message, string linkLabel, string url)
    {
        var content = new ToastContentBuilder()
            .AddText(AppIdentity.AppName)
            .AddText(message)
            // Protocol activation opens the URL in the default browser — no app-side activation handler needed.
            .AddButton(linkLabel, ToastActivationType.Protocol, url)
            .GetToastContent();

        var xml = new XmlDocument();
        xml.LoadXml(content.GetContent());

        // Distinct tag so it isn't replaced by routine status toasts before it can be clicked.
        var toast = new ToastNotification(xml) { Tag = "about", Group = Group };
        ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
    }
}
