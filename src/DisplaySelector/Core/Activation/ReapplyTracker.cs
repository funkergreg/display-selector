namespace DisplaySelector.Core.Activation;

/// <summary>
/// The "press again to re-apply" rule. Switching to a profile that's already live is skipped; asking
/// for the same profile again within <see cref="Window"/> of that skip forces a full re-apply (the
/// "unstick a frozen Windows display" fix stays one deliberate repeat away). Clock injected for tests.
/// </summary>
public sealed class ReapplyTracker
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

    private readonly Func<DateTime> _now;
    private string? _skippedId;
    private DateTime _skippedAt;

    public ReapplyTracker(Func<DateTime>? now = null)
    {
        _now = now ?? (() => DateTime.UtcNow);
    }

    /// <summary>True when this request repeats a just-skipped switch to the same profile.</summary>
    public bool ShouldForce(string profileId) =>
        _skippedId == profileId && _now() - _skippedAt <= Window;

    /// <summary>Remember a skipped switch, so a quick repeat forces it.</summary>
    public void RecordSkip(string profileId)
    {
        _skippedId = profileId;
        _skippedAt = _now();
    }

    /// <summary>Any applied switch ends the repeat window.</summary>
    public void Reset() => _skippedId = null;
}
