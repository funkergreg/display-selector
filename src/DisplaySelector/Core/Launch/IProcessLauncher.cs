namespace DisplaySelector.Core.Launch;

/// <summary>Starts a game shortcut's target (exe, .lnk, .url, or a launcher URI such as <c>steam://</c>).</summary>
public interface IProcessLauncher
{
    /// <summary>Launches <paramref name="target"/>; returns false with a user-facing <paramref name="error"/> on failure.</summary>
    bool TryLaunch(string target, string? arguments, out string? error);
}
