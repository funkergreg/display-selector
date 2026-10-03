using DisplaySelector.Core.Logging;
using DisplaySelector.Core.Profiles;

namespace DisplaySelector.Core.Launch;

/// <summary>
/// Finds and removes the Launchers / Profile Shortcuts the app created for a profile, so deleting the
/// profile can offer to delete them too instead of leaving orphans. Only shortcuts recorded in
/// <c>shortcuts.txt</c> are considered (the ones the app wrote and that are still where it put them);
/// each is read back and its <c>--profile</c> resolved, so a renamed one still counts. Never throws.
/// </summary>
public sealed class ProfileShortcutCleanup
{
    private readonly ShortcutRegistry _registry;
    private readonly IShortcutWriter _shortcuts;
    private readonly ILog _log;

    public ProfileShortcutCleanup(ShortcutRegistry registry, IShortcutWriter shortcuts, ILog log)
    {
        _registry = registry;
        _shortcuts = shortcuts;
        _log = log;
    }

    /// <summary>Paths of the tracked shortcuts whose command switches to <paramref name="profile"/>.</summary>
    public IReadOnlyList<string> Find(Profile profile, IReadOnlyList<Profile> profiles) =>
        _registry.TrackedPaths()
            .Where(path => _shortcuts.TryReadArguments(path) is { } args &&
                           LaunchCommand.TryParse(args, out _) is { } command &&
                           ProfileResolver.Resolve(profiles, command.ProfileRef) == profile)
            .ToList();

    /// <summary>Deletes the given shortcuts best-effort; returns how many were removed.</summary>
    public int Delete(IReadOnlyList<string> paths)
    {
        var deleted = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                File.Delete(path);
                deleted.Add(path);
                _log.Info($"Deleted shortcut '{path}'.");
            }
            catch (Exception ex)
            {
                _log.Error($"Couldn't delete shortcut '{path}'.", ex);
            }
        }

        _registry.Forget(deleted);
        return deleted.Count;
    }
}
