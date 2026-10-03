using DisplaySelector.Core.Hotkeys;
using DisplaySelector.Core.Profiles;
using DisplaySelector.UI;

namespace DisplaySelector.App;

/// <summary>
/// How profiles are shown everywhere (tray menu, Profile Manager, pickers), shared so they always match
/// and reordering looks identical. The name the user chose IS the description, so device details are
/// deliberately left out; the <see cref="ProfileGlyphs"/> icon beside it marks audio-only Profiles.
/// </summary>
internal static class ProfileLabels
{
    /// <summary>Placeholder for an empty profile list.</summary>
    public const string NoProfiles = "(no Profiles yet)";

    /// <summary>Toast for a command that needs a profile when there are none.</summary>
    public const string NoProfilesHint = "No Profiles yet — save one first.";

    public static string Label(Profile profile) =>
        profile.Hotkey is null
            ? $"{profile.Name} : No Hotkey"
            : $"{profile.Name} : {HotkeyCodec.Format(profile.Hotkey)}";

    /// <summary>The profile picker; with no profiles yet it says so instead and returns null.</summary>
    public static Profile? Pick(string title, string prompt, IReadOnlyList<Profile> profiles, Action<string, ToolTipIcon> notify)
    {
        if (profiles.Count == 0)
        {
            notify(NoProfilesHint, ToolTipIcon.Info);
            return null;
        }

        return ListPickerDialog.Pick(title, prompt, profiles);
    }
}
