namespace DisplaySelector.Core.Display;

/// <summary>Outcome of applying a display configuration. Activation is best-effort and surfaces partial failure.</summary>
public sealed class DisplayApplyResult
{
    public bool Success { get; init; }

    /// <summary>Friendly names of saved targets that could not be matched to live hardware (e.g. physically disconnected).</summary>
    public IReadOnlyList<string> UnavailableTargets { get; init; } = Array.Empty<string>();

    public string? Error { get; init; }

    public static DisplayApplyResult Ok(IReadOnlyList<string>? unavailable = null) =>
        new() { Success = true, UnavailableTargets = unavailable ?? Array.Empty<string>() };

    public static DisplayApplyResult Fail(string error, IReadOnlyList<string>? unavailable = null) =>
        new() { Success = false, Error = error, UnavailableTargets = unavailable ?? Array.Empty<string>() };
}
