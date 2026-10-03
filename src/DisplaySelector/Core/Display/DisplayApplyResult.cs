using DisplaySelector.Core.Profiles;

namespace DisplaySelector.Core.Display;

/// <summary>Outcome of applying a display configuration. Activation is best-effort and surfaces partial failure.</summary>
public sealed class DisplayApplyResult
{
    public bool Success { get; init; }

    /// <summary>Saved targets that aren't connected right now (e.g. a TV that's powered off).</summary>
    public IReadOnlyList<DisplayTarget> UnavailableTargets { get; init; } = Array.Empty<DisplayTarget>();

    public string? Error { get; init; }

    public static DisplayApplyResult Ok(IReadOnlyList<DisplayTarget>? unavailable = null) =>
        new() { Success = true, UnavailableTargets = unavailable ?? Array.Empty<DisplayTarget>() };

    public static DisplayApplyResult Fail(string error, IReadOnlyList<DisplayTarget>? unavailable = null) =>
        new() { Success = false, Error = error, UnavailableTargets = unavailable ?? Array.Empty<DisplayTarget>() };
}
