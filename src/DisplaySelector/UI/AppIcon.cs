using System.Reflection;

namespace DisplaySelector.UI;

/// <summary>Loads the embedded app icon for window title bars and the tray. Cached for the process lifetime.</summary>
internal static class AppIcon
{
    private static readonly Lazy<Icon?> FullIcon = new(() => Load(null));
    private static readonly Lazy<Icon?> SmallIcon = new(() => Load(SystemInformation.SmallIconSize));

    /// <summary>Multi-size icon for dialog title bars (Windows picks the size).</summary>
    public static Icon? Window => FullIcon.Value;

    /// <summary>Small icon sized for the system tray.</summary>
    public static Icon Tray => SmallIcon.Value ?? SystemIcons.Application;

    private static Icon? Load(Size? size)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resource = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase));
            if (resource is null)
            {
                return null;
            }

            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream is null)
            {
                return null;
            }

            return size is { } sz ? new Icon(stream, sz) : new Icon(stream);
        }
        catch
        {
            return null;
        }
    }
}
