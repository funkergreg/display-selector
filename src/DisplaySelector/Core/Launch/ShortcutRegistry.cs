using System.Text;
using DisplaySelector.Core.Logging;

namespace DisplaySelector.Core.Launch;

/// <summary>
/// Records the <c>.lnk</c> files the app created in <c>shortcuts.txt</c> (one full path per line,
/// UTF-8 with BOM) so the uninstaller can remove them best-effort. Plain text so Inno Setup's Pascal
/// can read it with <c>LoadStringsFromFile</c>. Never throws: a failed write must not block creating
/// the shortcut itself.
/// </summary>
public sealed class ShortcutRegistry
{
    private static readonly Encoding Utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    private readonly string _file;
    private readonly ILog _log;

    public ShortcutRegistry(string file, ILog log)
    {
        _file = file;
        _log = log;
    }

    /// <summary>The recorded shortcut paths that still exist. Never throws.</summary>
    public IReadOnlyList<string> TrackedPaths()
    {
        try
        {
            return LiveEntries().ToList();
        }
        catch (Exception ex)
        {
            _log.Error($"Couldn't read {_file}.", ex);
            return Array.Empty<string>();
        }
    }

    /// <summary>Stops tracking the given paths (after the app deleted them). Never throws.</summary>
    public void Forget(IReadOnlyCollection<string> shortcutPaths)
    {
        try
        {
            if (!File.Exists(_file))
            {
                return;
            }

            WriteAtomically(ReadEntries().Where(l => !shortcutPaths.Contains(l, StringComparer.OrdinalIgnoreCase)).ToList());
        }
        catch (Exception ex)
        {
            _log.Error($"Couldn't update {_file}.", ex);
        }
    }

    public void Record(string shortcutPath)
    {
        try
        {
            var path = Path.GetFullPath(shortcutPath);

            // Drop entries whose shortcut no longer exists (deleted/moved — the user's now) and
            // de-duplicate, so the list only ever holds live paths the app wrote.
            var kept = LiveEntries()
                .Where(l => !string.Equals(l, path, StringComparison.OrdinalIgnoreCase))
                .Append(path)
                .ToList();

            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            WriteAtomically(kept);
            _log.Debug($"Recorded shortcut '{path}' in {_file} ({kept.Count} tracked).");
        }
        catch (Exception ex)
        {
            _log.Error($"Couldn't record shortcut '{shortcutPath}' for uninstall cleanup.", ex);
        }
    }

    // Trimmed, non-blank lines (none when there's no file yet). Throws on IO errors; callers catch, so a
    // failed read never turns into a write that drops entries.
    private IEnumerable<string> ReadEntries() =>
        File.Exists(_file)
            ? File.ReadAllLines(_file).Select(l => l.Trim()).Where(l => l.Length > 0)
            : Enumerable.Empty<string>();

    // The entries whose shortcut still exists, de-duplicated.
    private IEnumerable<string> LiveEntries() =>
        ReadEntries().Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase);

    // The uninstaller depends on this list: write a temp file, then swap it in, so a crash or a locked
    // file mid-write can't truncate it.
    private void WriteAtomically(IEnumerable<string> lines)
    {
        var temp = _file + ".tmp";
        File.WriteAllLines(temp, lines, Utf8WithBom);
        File.Move(temp, _file, overwrite: true);
    }
}
