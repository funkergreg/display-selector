using System.Diagnostics;
using DisplaySelector.Core.Display;
using DisplaySelector.Core.Logging;
using DisplaySelector.Core.Profiles;

namespace DisplaySelector.Core.Launch;

public enum SettleOutcome
{
    /// <summary>No wait was needed (audio-only profile, or the display apply failed).</summary>
    Skipped,
    Settled,
    TimedOut,

    /// <summary>A display the profile needs, missing at activation, just connected (e.g. the TV was turned on). Re-apply.</summary>
    DisplayConnected,
}

public sealed record SettleResult(SettleOutcome Outcome, int Polls)
{
    public static SettleResult Skipped { get; } = new(SettleOutcome.Skipped, 0);

    /// <summary>For <see cref="SettleOutcome.DisplayConnected"/>: which of the missing displays connected.</summary>
    public IReadOnlyList<string> ConnectedDisplayIds { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Waits for a just-applied display layout to settle before a game is launched. <c>SetDisplayConfig</c>
/// returns before Windows finishes re-laying-out the desktop, and a game started straight away can read
/// the old geometry and go fullscreen on the wrong screen.
/// <para>
/// Polls <see cref="IDisplayService.GetCurrentDisplays"/>. Settled when either:
/// (a) the live layout exactly matches the profile (displays + primary, plus resolution/orientation
/// where saved) on <see cref="ExactMatchPolls"/> consecutive identical polls; or
/// (b) a full match never comes for some other reason, but the layout has changed from the
/// pre-activation <c>baseline</c> and held for <see cref="ChangedPolls"/> consecutive identical polls.
/// Then a short grace period for the TV's HDMI handshake. Times out (the caller launches anyway) rather
/// than ever blocking the game. Delay is injected so tests run instantly.
/// </para>
/// <para>
/// While a display the profile needs is still disconnected (a TV that's off drops HDMI hot-plug-detect),
/// rule (b) is off: the wait holds the launch open so the user can turn the TV on, and returns
/// <see cref="SettleOutcome.DisplayConnected"/> as soon as it connects so the caller can re-apply.
/// </para>
/// </summary>
public sealed class DisplaySettleWaiter
{
    public const int ExactMatchPolls = 2;
    public const int ChangedPolls = 4;

    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly IDisplayService _display;
    private readonly ILog _log;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _grace;
    private readonly int _maxPolls;

    public DisplaySettleWaiter(
        IDisplayService display,
        ILog log,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        TimeSpan? pollInterval = null,
        TimeSpan? grace = null,
        TimeSpan? timeout = null)
    {
        _display = display;
        _log = log;
        _delay = delay ?? Task.Delay;
        _pollInterval = pollInterval ?? DefaultPollInterval;
        _grace = grace ?? DefaultGrace;
        _maxPolls = Math.Max(1, (int)Math.Ceiling((timeout ?? DefaultTimeout) / _pollInterval));
    }

    private TimeSpan Timeout => _pollInterval * _maxPolls;

    /// <summary>Snapshot of the live layout, taken before activation so the wait can tell when it changed.</summary>
    public string? CaptureBaseline()
    {
        try
        {
            return Snapshot(_display.GetCurrentDisplays());
        }
        catch (Exception ex)
        {
            _log.Debug($"Settle baseline query failed ({ex.Message}).");
            return null;
        }
    }

    /// <param name="target">The display configuration that was just applied.</param>
    /// <param name="baseline">The pre-activation snapshot from <see cref="CaptureBaseline"/> (null if unknown).</param>
    /// <param name="missingDisplayIds">Stable ids of profile displays that weren't connected at activation.</param>
    public async Task<SettleResult> WaitAsync(
        DisplayConfig target,
        string? baseline,
        IReadOnlyCollection<string> missingDisplayIds,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        string? previous = null;
        var streak = 0;

        for (var poll = 1; poll <= _maxPolls; poll++)
        {
            await _delay(_pollInterval, cancellationToken);

            if (missingDisplayIds.Count > 0)
            {
                var connected = ConnectedOf(missingDisplayIds, poll);
                if (connected.Count > 0)
                {
                    // Let the HDMI handshake finish before the caller re-applies onto the new display.
                    await _delay(_grace, cancellationToken);
                    _log.Info($"Missing display(s) {string.Join(", ", connected)} connected after {poll} poll(s), {stopwatch.ElapsedMilliseconds} ms.");
                    return new SettleResult(SettleOutcome.DisplayConnected, poll) { ConnectedDisplayIds = connected };
                }

                // A display the profile needs is still off: the layout can't match yet and rule (b) is
                // off, so there's no point querying it. Keep waiting for the display.
                continue;
            }

            string snapshot;
            bool exact;
            try
            {
                var current = _display.GetCurrentDisplays();
                snapshot = Snapshot(current);
                exact = ProfileMatching.ExactLayoutMatches(target, current);
            }
            catch (Exception ex)
            {
                // A query racing the mode change can fail; treat it as "still changing".
                _log.Debug($"Settle poll {poll}: display query failed ({ex.Message}).");
                previous = null;
                streak = 0;
                continue;
            }

            streak = snapshot == previous ? streak + 1 : 1;
            previous = snapshot;
            var changed = baseline is not null && snapshot != baseline;
            _log.Debug($"Settle poll {poll}: exact={exact} changed={changed} stable={streak} layout=[{snapshot}]");

            if ((exact && streak >= ExactMatchPolls) || (changed && streak >= ChangedPolls))
            {
                await _delay(_grace, cancellationToken);
                _log.Info($"Displays settled ({(exact ? "matches profile" : "changed, partial match")}) after {poll} poll(s), {stopwatch.ElapsedMilliseconds} ms.");
                return new SettleResult(SettleOutcome.Settled, poll);
            }
        }

        _log.Info($"Displays did not settle within {Timeout.TotalSeconds:0.#} s ({stopwatch.ElapsedMilliseconds} ms); continuing anyway.");
        return new SettleResult(SettleOutcome.TimedOut, _maxPolls);
    }

    private IReadOnlyList<string> ConnectedOf(IReadOnlyCollection<string> displayIds, int poll)
    {
        try
        {
            var connected = _display.GetConnectedTargetIds();
            return displayIds.Where(connected.Contains).ToList();
        }
        catch (Exception ex)
        {
            _log.Debug($"Settle poll {poll}: connected-display query failed ({ex.Message}).");
            return Array.Empty<string>();
        }
    }

    private static string Snapshot(IReadOnlyList<DisplayTarget> displays) =>
        string.Join(";", displays
            .Select(d => $"{d.StableId}|{(d.Primary ? "P" : "-")}|{d.Resolution}|{d.Orientation}")
            .OrderBy(x => x, StringComparer.Ordinal));
}
