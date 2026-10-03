namespace DisplaySelector.Core.Launch;

/// <summary>Default names and file-name sanitizing for game shortcuts created from the tray.</summary>
public static class ShortcutNaming
{
    /// <summary><c>"&lt;target&gt; (&lt;profile&gt;)"</c>, or just the profile name for a switch-only shortcut.</summary>
    public static string DefaultName(string profileName, string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return Sanitize(profileName);
        }

        var targetName = LaunchCommand.IsUri(target) ? "Game" : LaunchCommand.DisplayNameOf(target);
        return Sanitize($"{targetName} ({profileName})");
    }

    /// <summary>Strips characters Windows doesn't allow in file names, plus trailing dots/spaces.</summary>
    public static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return cleaned.Trim().TrimEnd('.', ' ');
    }
}
