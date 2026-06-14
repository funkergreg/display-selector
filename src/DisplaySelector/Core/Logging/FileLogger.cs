using System.Text;

namespace DisplaySelector.Core.Logging;

/// <summary>
/// Hand-rolled, size-capped rolling file logger (no external dependency). Thread-safe.
/// Rolls <c>displayselector.log</c> to numbered archives (<c>displayselector.1.log</c> …)
/// when it would exceed <paramref name="maxBytes"/>, keeping at most <paramref name="maxFiles"/>.
/// </summary>
public sealed class FileLogger : ILog
{
    private const string BaseName = "displayselector";

    private readonly string _directory;
    private readonly string _currentFile;
    private readonly long _maxBytes;
    private readonly int _maxFiles;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();

    public LogLevel Level { get; set; }

    public FileLogger(
        string logDirectory,
        LogLevel level = LogLevel.Info,
        long maxBytes = 1_048_576,
        int maxFiles = 5,
        Func<DateTimeOffset>? clock = null)
    {
        _directory = logDirectory;
        Directory.CreateDirectory(_directory);
        _currentFile = Path.Combine(_directory, BaseName + ".log");
        _maxBytes = maxBytes;
        _maxFiles = Math.Max(1, maxFiles);
        _clock = clock ?? (() => DateTimeOffset.Now);
        Level = level;
    }

    public void Info(string message) => Write("INFO", message);

    public void Debug(string message)
    {
        if (Level == LogLevel.Debug)
        {
            Write("DEBUG", message);
        }
    }

    public void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}{Environment.NewLine}{ex}");

    private void Write(string level, string message)
    {
        var line = $"{_clock():yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
        var bytes = Encoding.UTF8.GetByteCount(line);
        lock (_gate)
        {
            try
            {
                // Recreate the directory if it vanished (e.g. uninstall deleted it while running),
                // and never let a logging failure crash the app.
                Directory.CreateDirectory(_directory);
                RollIfNeeded(bytes);
                File.AppendAllText(_currentFile, line, Encoding.UTF8);
            }
            catch
            {
                // Logging is best-effort; swallow IO errors.
            }
        }
    }

    private void RollIfNeeded(int incomingBytes)
    {
        var info = new FileInfo(_currentFile);
        if (!info.Exists || info.Length == 0)
        {
            return;
        }

        if (info.Length + incomingBytes <= _maxBytes)
        {
            return;
        }

        // Drop the oldest archive, shift the rest up, then archive the current file as .1.
        var oldest = Numbered(_maxFiles - 1);
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var i = _maxFiles - 2; i >= 1; i--)
        {
            var src = Numbered(i);
            if (File.Exists(src))
            {
                File.Move(src, Numbered(i + 1));
            }
        }

        File.Move(_currentFile, Numbered(1));
    }

    private string Numbered(int n) => Path.Combine(_directory, $"{BaseName}.{n}.log");
}
