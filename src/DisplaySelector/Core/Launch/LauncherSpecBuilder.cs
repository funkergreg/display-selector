using DisplaySelector.Core.Profiles;

namespace DisplaySelector.Core.Launch;

/// <summary>
/// Builds the <see cref="ShortcutSpec"/> for a launcher: a desktop <c>.lnk</c> that runs the app with
/// <c>--profile &lt;id&gt; [--launch &lt;target&gt; [--args …]]</c>. A null target makes a switch-only
/// launcher. Pure, so the shape is unit-tested. The profile is referenced by Id so renaming it later
/// doesn't break the launcher.
/// </summary>
public static class LauncherSpecBuilder
{
    public static ShortcutSpec Build(
        Profile profile,
        string? target,
        string? arguments,
        string name,
        string exePath,
        string desktopDirectory)
    {
        var command = new LaunchCommand(profile.Id, target, target is null ? null : arguments);
        var iconPath = target is not null && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(target)
            ? target
            : exePath;
        var description = target is null
            ? $"Switch to {profile.Name} ({AppIdentity.AppName})"
            : $"Switch to {profile.Name} and launch {command.TargetDisplayName} ({AppIdentity.AppName})";

        return new ShortcutSpec(
            Path.Combine(desktopDirectory, name + ".lnk"),
            exePath,
            command.ToArgumentString(),
            Path.GetDirectoryName(exePath),
            description,
            iconPath);
    }
}
