namespace DisplaySelector.Core;

/// <summary>
/// Canonical on-disk locations. Non-roaming LocalApplicationData because profiles are
/// hardware-specific to this machine (see CLAUDE.md "Data &amp; storage").
/// </summary>
public static class AppPaths
{
    public const string AppFolderName = "DisplaySelector";

    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolderName);

    public static string LogsDirectory => Path.Combine(DataDirectory, "logs");

    public static string ProfilesFile => Path.Combine(DataDirectory, "profiles.json");

    public static string ConfigFile => Path.Combine(DataDirectory, "config.json");
}
