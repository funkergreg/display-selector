using System.Text;
using DisplaySelector.Core.Audio;
using DisplaySelector.Core.Display;
using DisplaySelector.Core.Hotkeys;
using DisplaySelector.Core.Profiles;

namespace DisplaySelector.Core;

/// <summary>
/// Plain-text diagnostics for one Profile (Profile Manager ▸ Show diagnostics): what it saved — displays,
/// audio device, hotkey — next to whether each is there right now, plus the desktop Launchers / Profile
/// Shortcuts that use it (the ones Delete Profile offers to remove). Displays use the same one-line form
/// as Copy diagnostics.
/// </summary>
public static class ProfileReport
{
    public static string Build(
        Profile profile,
        bool isActive,
        IReadOnlyList<DisplayTarget> activeDisplays,
        IReadOnlyList<DisplayTarget> connectedDisplays,
        IReadOnlyList<AudioEndpoint> audioDevices,
        IReadOnlyList<string> shortcutPaths)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Profile : {profile.Name}");
        sb.AppendLine($"Status  : {(isActive ? "Active (check-marked)" : "Not active")}");
        sb.AppendLine($"Hotkey  : {(profile.Hotkey is null ? "None" : HotkeyCodec.Format(profile.Hotkey))}");
        sb.AppendLine($"Kind    : {(profile.IsAudioOnly ? "Audio only (leaves the displays as they are)" : "Displays + audio")}");

        sb.AppendLine();
        sb.AppendLine("Displays (saved in the Profile | how they are now):");
        if (profile.Display is not { Targets.Count: > 0 } display)
        {
            sb.AppendLine("  (none)");
        }
        else
        {
            foreach (var target in display.Targets)
            {
                var state = DisplayInventory.StateOf(target.StableId, activeDisplays, connectedDisplays);
                sb.AppendLine($"  {DisplayInventory.Line(target)} | {DisplayInventory.Describe(state)}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Audio device (saved in the Profile | how it is now):");
        if (profile.Audio is not { EndpointId.Length: > 0 } audio)
        {
            sb.AppendLine("  (none)");
        }
        else
        {
            var device = audioDevices.FirstOrDefault(d => d.Id == audio.EndpointId);
            var status = device is null ? "Not available (off or unplugged)"
                : device.IsDefault ? "Windows is playing to it now"
                : "Available";
            sb.AppendLine($"  {audio.FriendlyName} | {status}");
            sb.AppendLine($"  id: {audio.EndpointId}");
        }

        sb.AppendLine();
        sb.AppendLine("Desktop Launchers and Profile Shortcuts that use it:");
        if (shortcutPaths.Count == 0)
        {
            sb.AppendLine("  (none found)");
        }

        foreach (var path in shortcutPaths)
        {
            sb.AppendLine($"  {Path.GetFileNameWithoutExtension(path)} | {path}");
        }

        return sb.ToString();
    }
}
