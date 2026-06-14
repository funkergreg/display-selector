using DisplaySelector.Core.Logging;
using Microsoft.Win32;

namespace DisplaySelector.Core.Startup;

/// <summary>
/// Auto-start via <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>. Per-user (no admin).
/// The installer declares the same value with <c>uninsdeletevalue</c> so uninstall removes it too.
/// </summary>
public sealed class RunKeyAutoStart : IAutoStartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DisplaySelector";

    private readonly ILog _log;

    public RunKeyAutoStart(ILog log) => _log = log;

    public bool IsEnabled()
    {
        if (PackageContext.IsPackaged)
        {
            return false; // MSIX uses a StartupTask, not the Run key (see docs/STORE.md).
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrEmpty(value);
    }

    public void Enable()
    {
        if (PackageContext.IsPackaged)
        {
            _log.Info("Packaged (MSIX) context: auto-start must use a StartupTask, not the Run key — skipping.");
            return;
        }

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            _log.Error("Cannot enable auto-start: executable path is unknown.");
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(ValueName, $"\"{exePath}\"");
        _log.Info($"Auto-start enabled ({exePath}).");
    }

    public void Disable()
    {
        if (PackageContext.IsPackaged)
        {
            return;
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
        _log.Info("Auto-start disabled.");
    }
}
