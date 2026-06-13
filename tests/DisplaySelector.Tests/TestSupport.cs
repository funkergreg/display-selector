using DisplaySelector.Core.Logging;

namespace DisplaySelector.Tests;

/// <summary>A throwaway temp directory, removed on dispose.</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "DisplaySelectorTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; ignore locked-file races on CI.
        }
    }
}

/// <summary>No-op logger for store tests that don't assert on log output.</summary>
internal sealed class NullLog : ILog
{
    public LogLevel Level { get; set; } = LogLevel.Info;

    public void Info(string message)
    {
    }

    public void Debug(string message)
    {
    }

    public void Error(string message, Exception? ex = null)
    {
    }
}
