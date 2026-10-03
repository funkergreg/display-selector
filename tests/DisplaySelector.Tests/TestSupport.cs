using DisplaySelector.Core.Display;
using DisplaySelector.Core.Logging;
using DisplaySelector.Core.Profiles;

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

/// <summary>Display fake: returns a scripted sequence of live layouts (the last one repeats) and records applies.</summary>
internal sealed class ScriptedDisplayService : IDisplayService
{
    private readonly IReadOnlyList<DisplayTarget>[] _script;

    public ScriptedDisplayService(params IReadOnlyList<DisplayTarget>[] script)
    {
        _script = script;
    }

    public int Calls { get; private set; }

    /// <summary>1-based call numbers on which <see cref="GetCurrentDisplays"/> throws.</summary>
    public HashSet<int> ThrowOnCalls { get; } = new();

    public DisplayApplyResult ApplyResult { get; set; } = DisplayApplyResult.Ok();

    public IReadOnlyList<DisplayTarget> GetCurrentDisplays()
    {
        Calls++;
        if (ThrowOnCalls.Contains(Calls))
        {
            throw new InvalidOperationException("query raced the mode change");
        }

        return _script[Math.Min(Calls - 1, _script.Length - 1)];
    }

    /// <summary>A display that shows up as connected from the given (1-based) connected-query onwards.</summary>
    public (int FromQuery, string Id)? Connects { get; set; }

    private int _connectedQueries;

    public IReadOnlySet<string> GetConnectedTargetIds()
    {
        _connectedQueries++;
        return Connects is { } c && _connectedQueries >= c.FromQuery
            ? new HashSet<string> { c.Id }
            : new HashSet<string>();
    }

    public DisplayConfig Capture() => new();

    public bool ValidateCurrent() => true;

    public DisplayApplyResult ReapplyCurrent() => DisplayApplyResult.Ok();

    public int ApplyCount { get; private set; }

    public DisplayApplyResult Apply(DisplayConfig config)
    {
        ApplyCount++;
        return ApplyResult;
    }

    /// <summary>The detailed (blob-level) layout comparison: positions, duplicate/extend, refresh rate.</summary>
    public bool MatchesResult { get; set; } = true;

    public bool MatchesCurrent(DisplayConfig config) => MatchesResult;
}
