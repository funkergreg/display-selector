using DisplaySelector.Core.Launch;
using DisplaySelector.Core.Logging;
using DisplaySelector.Core.Profiles;
using DisplaySelector.UI;

namespace DisplaySelector.App;

/// <summary>
/// The "Create a Launcher…" flows, sharing one write → record → notify tail. The Launcher flow starts
/// with the file chooser: per profile (tray submenu / Profile Manager right pane) the profile is already
/// known; from the Profile Manager bottom bar the profile is picked after the executable. "Create a
/// Profile Shortcut" (per profile) skips both the chooser and the confirm dialog: it only switches
/// profiles, so it's created straight away, named after the profile.
/// </summary>
internal sealed class LauncherCreator
{
    private readonly IShortcutWriter _writer;
    private readonly ShortcutRegistry _registry;
    private readonly ILog _log;
    private readonly Action<string, ToolTipIcon> _notify;
    private readonly Action<string, Exception> _reportFailure;

    public LauncherCreator(
        IShortcutWriter writer,
        ShortcutRegistry registry,
        ILog log,
        Action<string, ToolTipIcon> notify,
        Action<string, Exception> reportFailure)
    {
        _writer = writer;
        _registry = registry;
        _log = log;
        _notify = notify;
        _reportFailure = reportFailure;
    }

    public void CreateForProfile(Profile profile, IWin32Window? owner)
    {
        var target = LauncherTargetPicker.Pick(owner);
        if (target is not null)
        {
            ConfirmAndWrite(profile, target, owner);
        }
    }

    public void CreateProfileShortcut(Profile profile, IWin32Window? owner)
    {
        var name = ShortcutNaming.DefaultName(profile.Name, target: null);
        Write(profile, target: null, arguments: null, name.Length > 0 ? name : "Profile Shortcut", owner);
    }

    public void CreateAndAssign(IReadOnlyList<Profile> profiles, IWin32Window? owner)
    {
        // Check before the file chooser, so nobody picks a game only to learn there's nothing to assign.
        if (profiles.Count == 0)
        {
            _notify(ProfileLabels.NoProfilesHint, ToolTipIcon.Info);
            return;
        }

        var target = LauncherTargetPicker.Pick(owner);
        if (target is null)
        {
            return;
        }

        var profile = ProfileLabels.Pick(
            "Create a Launcher for Profile",
            $"Which Profile should '{LaunchCommand.DisplayNameOf(target)}' switch to?",
            profiles,
            _notify);
        if (profile is not null)
        {
            ConfirmAndWrite(profile, target, owner);
        }
    }

    private void ConfirmAndWrite(Profile profile, string target, IWin32Window? owner)
    {
        using var details = new LauncherDetailsDialog(profile.Name, target);
        if (details.ShowDialog(owner) == DialogResult.OK)
        {
            Write(profile, target, details.Arguments, details.LauncherName, owner);
        }
    }

    // A null target is a Profile Shortcut (switch only); otherwise a Launcher.
    private void Write(Profile profile, string? target, string? arguments, string name, IWin32Window? owner)
    {
        var noun = LaunchCommand.NounFor(target);
        var spec = LauncherSpecBuilder.Build(
            profile,
            target,
            arguments,
            name,
            Application.ExecutablePath,
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));

        if (File.Exists(spec.Path) &&
            MessageBox.Show(
                owner,
                $"'{name}' is already on the desktop. Replace it?",
                $"{noun} exists",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question) != DialogResult.OK)
        {
            return;
        }

        try
        {
            _writer.Write(spec);
        }
        catch (Exception ex)
        {
            _reportFailure($"create the {noun}", ex);
            return;
        }

        _registry.Record(spec.Path);
        _log.Info($"Created {noun} '{spec.Path}' for profile '{profile.Name}'{(target is null ? string.Empty : $" launching '{target}'")}.");
        _notify($"Created {noun} '{name}' on the desktop.", ToolTipIcon.Info);
    }
}
