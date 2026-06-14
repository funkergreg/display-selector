using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using DisplaySelector.Core.Audio;
using DisplaySelector.Core.Display;

namespace DisplaySelector.Core;

/// <summary>Builds a plain-text environment + inventory report for pasting into bug reports.</summary>
public static class DiagnosticsReport
{
    public static string Build(IDisplayService display, IAudioService audio)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{AppIdentity.AppName} diagnostics");
        sb.AppendLine($"App version : {Assembly.GetExecutingAssembly().GetName().Version}");
        sb.AppendLine($"OS          : {RuntimeInformation.OSDescription}");
        sb.AppendLine($".NET        : {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"Packaged    : {PackageContext.IsPackaged}");

        sb.AppendLine();
        sb.AppendLine("Graphics adapters:");
        var adapters = GetAdapters();
        if (adapters.Count == 0)
        {
            sb.AppendLine("  (none reported)");
        }

        foreach (var adapter in adapters)
        {
            sb.AppendLine($"  - {adapter}");
        }

        sb.AppendLine();
        sb.AppendLine("Displays (current):");
        foreach (var d in display.GetCurrentDisplays())
        {
            var primary = d.Primary ? " | PRIMARY" : string.Empty;
            sb.AppendLine($"  - {d.Friendly} | port={d.StableId} | {d.Resolution} | {d.Orientation}{primary}");
        }

        sb.AppendLine();
        sb.AppendLine("Audio output devices:");
        foreach (var a in audio.GetOutputDevices())
        {
            var def = a.IsDefault ? " (default)" : string.Empty;
            sb.AppendLine($"  - {a.FriendlyName}{def} | {a.Id}");
        }

        return sb.ToString();
    }

    private static List<string> GetAdapters()
    {
        var structSize = Marshal.SizeOf<DISPLAY_DEVICE>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var adapters = new List<string>();

        var device = new DISPLAY_DEVICE { cb = structSize };
        for (uint i = 0; EnumDisplayDevices(null, i, ref device, 0); i++)
        {
            if (!string.IsNullOrWhiteSpace(device.DeviceString) && seen.Add(device.DeviceString))
            {
                adapters.Add(device.DeviceString);
            }

            device.cb = structSize;
        }

        return adapters;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public int StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }
}
