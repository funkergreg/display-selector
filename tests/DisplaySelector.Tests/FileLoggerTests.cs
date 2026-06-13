using DisplaySelector.Core.Logging;
using Xunit;

namespace DisplaySelector.Tests;

public class FileLoggerTests
{
    private static string CurrentLog(string dir) => Path.Combine(dir, "displayselector.log");

    [Fact]
    public void Debug_is_suppressed_at_Info_level()
    {
        using var tmp = new TempDir();
        var log = new FileLogger(tmp.Path, LogLevel.Info);

        log.Debug("secret-shape");
        log.Info("hello");

        var content = File.ReadAllText(CurrentLog(tmp.Path));
        Assert.DoesNotContain("secret-shape", content);
        Assert.Contains("hello", content);
    }

    [Fact]
    public void Debug_is_written_at_Debug_level()
    {
        using var tmp = new TempDir();
        var log = new FileLogger(tmp.Path, LogLevel.Debug);

        log.Debug("visible-shape");

        Assert.Contains("visible-shape", File.ReadAllText(CurrentLog(tmp.Path)));
    }

    [Fact]
    public void Error_includes_exception_detail()
    {
        using var tmp = new TempDir();
        var log = new FileLogger(tmp.Path, LogLevel.Info);

        log.Error("boom", new InvalidOperationException("kaboom"));

        var content = File.ReadAllText(CurrentLog(tmp.Path));
        Assert.Contains("boom", content);
        Assert.Contains("kaboom", content);
    }

    [Fact]
    public void Rolls_and_caps_archive_count_when_exceeding_max_bytes()
    {
        using var tmp = new TempDir();
        var log = new FileLogger(tmp.Path, LogLevel.Info, maxBytes: 256, maxFiles: 3);

        for (var i = 0; i < 200; i++)
        {
            log.Info($"line {i} ........................................");
        }

        var files = Directory.GetFiles(tmp.Path, "displayselector*.log");
        Assert.True(files.Length > 1, "expected at least one rolled archive");
        Assert.True(files.Length <= 3, "must not exceed maxFiles");
        Assert.True(File.Exists(CurrentLog(tmp.Path)), "current log must exist after rolling");
    }
}
