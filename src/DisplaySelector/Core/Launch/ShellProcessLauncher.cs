using System.ComponentModel;
using System.Diagnostics;
using DisplaySelector.Core.Logging;

namespace DisplaySelector.Core.Launch;

/// <summary>
/// Launches through <c>ShellExecute</c>, so one code path handles executables, shortcuts, <c>.url</c>
/// files and launcher URIs (<c>steam://rungameid/…</c>, <c>com.epicgames.launcher://…</c>) with no
/// launcher-specific code. UAC prompts for elevated games work as usual.
/// </summary>
public sealed class ShellProcessLauncher : IProcessLauncher
{
    private const int ErrorCancelled = 1223; // the user dismissed the UAC prompt

    private readonly ILog _log;

    public ShellProcessLauncher(ILog log)
    {
        _log = log;
    }

    public bool TryLaunch(string target, string? arguments, out string? error)
    {
        var fileName = Environment.ExpandEnvironmentVariables(target);
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments ?? string.Empty,
            UseShellExecute = true,
        };

        // Games often load assets relative to the working directory; default it to the target's folder.
        if (Path.IsPathFullyQualified(fileName) && File.Exists(fileName))
        {
            startInfo.WorkingDirectory = Path.GetDirectoryName(fileName) ?? string.Empty;
        }

        try
        {
            _log.Info($"Launching '{fileName}'{(string.IsNullOrEmpty(arguments) ? string.Empty : $" with arguments '{arguments}'")}.");
            using var process = Process.Start(startInfo); // null for URIs handed to an existing process
            error = null;
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            _log.Info($"Launch of '{fileName}' was cancelled at the elevation prompt.");
            error = "the launch was cancelled";
            return false;
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to launch '{fileName}'.", ex);
            error = ex.Message;
            return false;
        }
    }
}
