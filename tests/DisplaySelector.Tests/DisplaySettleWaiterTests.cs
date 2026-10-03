using DisplaySelector.Core.Display;
using DisplaySelector.Core.Launch;
using DisplaySelector.Core.Profiles;
using Xunit;

namespace DisplaySelector.Tests;

public class DisplaySettleWaiterTests
{
    private static readonly DisplayTarget TvPrimary = new() { StableId = "Hdmi:0", Primary = true, Resolution = "3840x2160" };
    private static readonly DisplayTarget DeskPrimary = new() { StableId = "Dp:1", Primary = true, Resolution = "2560x1440" };
    private static readonly DisplayConfig TvLayout = new() { Targets = { new DisplayTarget { StableId = "Hdmi:0", Primary = true } } };
    private static readonly string[] NoneMissing = Array.Empty<string>();
    private static readonly string[] TvMissing = { "Hdmi:0" };

    // 250 ms polls with a 2.5 s timeout → at most 10 polls; the delay is instant.
    private static DisplaySettleWaiter Waiter(IDisplayService display, List<TimeSpan>? delays = null) =>
        new(
            display,
            new NullLog(),
            (span, token) =>
            {
                token.ThrowIfCancellationRequested();
                delays?.Add(span);
                return Task.CompletedTask;
            },
            pollInterval: TimeSpan.FromMilliseconds(250),
            grace: TimeSpan.FromMilliseconds(500),
            timeout: TimeSpan.FromMilliseconds(2500));

    // The pre-activation snapshot the coordinator would capture for this live layout.
    private static string? Baseline(params DisplayTarget[] live) => Waiter(new ScriptedDisplayService(live)).CaptureBaseline();

    [Fact]
    public async Task Settles_after_two_identical_matching_polls_then_waits_the_grace_period()
    {
        var display = new ScriptedDisplayService(new[] { TvPrimary });
        var delays = new List<TimeSpan>();

        var result = await Waiter(display, delays).WaitAsync(TvLayout, Baseline(DeskPrimary), NoneMissing, CancellationToken.None);

        Assert.Equal(SettleOutcome.Settled, result.Outcome);
        Assert.Equal(2, result.Polls);
        Assert.Equal(TimeSpan.FromMilliseconds(500), delays[^1]);
    }

    [Fact]
    public async Task Waits_through_the_transition_until_the_new_layout_holds()
    {
        var display = new ScriptedDisplayService(
            new[] { DeskPrimary },                         // old layout still reported
            new[] { DeskPrimary },
            new[] { TvPrimary, new DisplayTarget { StableId = "Dp:1", Resolution = "2560x1440" } }, // mid-transition: extra display
            new[] { TvPrimary },
            new[] { TvPrimary });

        var result = await Waiter(display).WaitAsync(TvLayout, Baseline(DeskPrimary), NoneMissing, CancellationToken.None);

        Assert.Equal(SettleOutcome.Settled, result.Outcome);
        Assert.Equal(5, result.Polls);
    }

    [Fact]
    public async Task Stale_polls_still_showing_the_old_layout_never_count_as_settled()
    {
        // Right after SetDisplayConfig the old layout can still be reported, identically, for a while.
        var display = new ScriptedDisplayService(new[] { DeskPrimary });

        var result = await Waiter(display).WaitAsync(TvLayout, Baseline(DeskPrimary), NoneMissing, CancellationToken.None);

        Assert.Equal(SettleOutcome.TimedOut, result.Outcome);
        Assert.Equal(10, display.Calls);
    }

    [Fact]
    public async Task Resolution_only_profile_waits_for_the_new_mode()
    {
        var tv1080 = new DisplayTarget { StableId = "Hdmi:0", Primary = true, Resolution = "1920x1080" };
        var tv4kProfile = new DisplayConfig { Targets = { new DisplayTarget { StableId = "Hdmi:0", Primary = true, Resolution = "3840x2160" } } };
        var display = new ScriptedDisplayService(new[] { tv1080 }, new[] { tv1080 }, new[] { TvPrimary }, new[] { TvPrimary });

        var result = await Waiter(display).WaitAsync(tv4kProfile, Baseline(tv1080), NoneMissing, CancellationToken.None);

        Assert.Equal(SettleOutcome.Settled, result.Outcome);
        Assert.Equal(4, result.Polls); // not 2: the identical old-mode polls don't match the saved 4K mode
    }

    [Fact]
    public async Task Changed_but_partial_layout_settles_after_a_longer_stable_run()
    {
        // Every display is connected, but a full match never comes (e.g. Windows wouldn't take the saved
        // mode) — the apply visibly changed the layout, and it holds.
        var deskReconfigured = new DisplayTarget { StableId = "Dp:1", Primary = true, Resolution = "1920x1080" };
        var display = new ScriptedDisplayService(new[] { deskReconfigured });

        var result = await Waiter(display).WaitAsync(TvLayout, Baseline(DeskPrimary), NoneMissing, CancellationToken.None);

        Assert.Equal(SettleOutcome.Settled, result.Outcome);
        Assert.Equal(DisplaySettleWaiter.ChangedPolls, result.Polls);
    }

    [Fact]
    public async Task Missing_display_holds_the_wait_open_instead_of_settling_on_the_partial_layout()
    {
        // The TV is off: the desk-only layout changed and holds, but that must not end the wait.
        var deskReconfigured = new DisplayTarget { StableId = "Dp:1", Primary = true, Resolution = "1920x1080" };
        var display = new ScriptedDisplayService(new[] { deskReconfigured });

        var result = await Waiter(display).WaitAsync(TvLayout, Baseline(DeskPrimary), TvMissing, CancellationToken.None);

        Assert.Equal(SettleOutcome.TimedOut, result.Outcome);
    }

    [Fact]
    public async Task Missing_display_connecting_returns_so_the_caller_can_reapply()
    {
        var display = new ScriptedDisplayService(new[] { DeskPrimary }) { Connects = (3, "Hdmi:0") };
        var delays = new List<TimeSpan>();

        var result = await Waiter(display, delays).WaitAsync(TvLayout, Baseline(DeskPrimary), TvMissing, CancellationToken.None);

        Assert.Equal(SettleOutcome.DisplayConnected, result.Outcome);
        Assert.Equal(3, result.Polls);
        Assert.Equal(new[] { "Hdmi:0" }, result.ConnectedDisplayIds);
        Assert.Equal(TimeSpan.FromMilliseconds(500), delays[^1]); // handshake grace before re-applying
    }

    [Fact]
    public async Task A_failing_query_counts_as_still_changing()
    {
        var display = new ScriptedDisplayService(new[] { TvPrimary }) { ThrowOnCalls = { 2 } };

        var result = await Waiter(display).WaitAsync(TvLayout, Baseline(DeskPrimary), NoneMissing, CancellationToken.None);

        Assert.Equal(SettleOutcome.Settled, result.Outcome);
        Assert.Equal(4, result.Polls); // poll 1 ok, 2 throws (resets), 3 ok, 4 ok = settled
    }

    [Fact]
    public async Task Cancellation_stops_the_wait()
    {
        var display = new ScriptedDisplayService(new[] { DeskPrimary });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Waiter(display).WaitAsync(TvLayout, Baseline(DeskPrimary), NoneMissing, cancellation.Token));
    }

    [Fact]
    public void Exact_match_compares_saved_resolution_and_orientation_only_when_present()
    {
        var live = new[] { new DisplayTarget { StableId = "Hdmi:0", Primary = true, Resolution = "3840x2160", Orientation = "Identity" } };

        Assert.True(ProfileMatching.ExactLayoutMatches(TvLayout, live)); // older capture: no resolution saved
        Assert.False(ProfileMatching.ExactLayoutMatches(
            new DisplayConfig { Targets = { new DisplayTarget { StableId = "Hdmi:0", Primary = true, Orientation = "Rotate90" } } },
            live));
    }
}
