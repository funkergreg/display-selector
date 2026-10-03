namespace DisplaySelector.Core.Launch;

/// <summary>Everything needed to write one Windows shortcut (<c>.lnk</c>).</summary>
public sealed record ShortcutSpec(
    string Path,
    string TargetPath,
    string Arguments,
    string? WorkingDirectory,
    string Description,
    string IconPath);

/// <summary>Writes (and reads back) <c>.lnk</c> files. Behind an interface so the shell COM interop stays isolated + mockable.</summary>
public interface IShortcutWriter
{
    /// <summary>Creates or overwrites the shortcut. Throws on failure.</summary>
    void Write(ShortcutSpec spec);

    /// <summary>
    /// A shortcut's arguments, split as Windows would pass them to the target; null when the file
    /// can't be read as a shortcut. Never throws.
    /// </summary>
    IReadOnlyList<string>? TryReadArguments(string path);
}
