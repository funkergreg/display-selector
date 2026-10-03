using DisplaySelector.Core.Audio;
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

/// <summary>No-op logger for tests that don't assert on log output; it only counts errors.</summary>
internal sealed class NullLog : ILog
{
    public LogLevel Level { get; set; } = LogLevel.Info;

    public int Errors { get; private set; }

    public void Info(string message)
    {
    }

    public void Debug(string message)
    {
    }

    public void Error(string message, Exception? ex = null) => Errors++;
}

/// <summary>
/// Audio fake for state queries: a settable device list and default device (else the listed device
/// marked default); optionally throws.
/// </summary>
internal sealed class StubAudioService : IAudioService
{
    public string? DefaultId { get; set; }

    public IReadOnlyList<AudioEndpoint> Devices { get; set; } = Array.Empty<AudioEndpoint>();

    public bool Throws { get; set; }

    public int DefaultQueries { get; private set; }

    public int DeviceQueries { get; private set; }

    public IReadOnlyList<AudioEndpoint> GetOutputDevices()
    {
        DeviceQueries++;
        return Devices;
    }

    public AudioEndpoint? GetDefaultOutputDevice()
    {
        DefaultQueries++;
        return Throws ? throw new InvalidOperationException("device arriving")
            : DefaultId is null ? Devices.FirstOrDefault(d => d.IsDefault) : new AudioEndpoint(DefaultId, DefaultId, true);
    }

    public bool IsDeviceActive(string endpointId) => true;

    public bool SetDefaultOutputDevice(string endpointId)
    {
        DefaultId = endpointId;
        return true;
    }

    public Task PlayConfirmationAsync(string? endpointId = null) => Task.CompletedTask;

    public event Action? DefaultDeviceChanged { add { } remove { } }
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

    public int ConnectedQueries { get; private set; }

    public IReadOnlyList<DisplayTarget> GetConnectedDisplays()
    {
        ConnectedQueries++;
        return Connects is { } c && ConnectedQueries >= c.FromQuery
            ? new[] { new DisplayTarget { StableId = c.Id } }
            : Array.Empty<DisplayTarget>();
    }

    public DisplayConfig Capture() => new();

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
