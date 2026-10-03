using DisplaySelector.Core.Activation;
using Xunit;

namespace DisplaySelector.Tests;

public class ReapplyTrackerTests
{
    private DateTime _now = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);

    private ReapplyTracker Tracker() => new(() => _now);

    [Fact]
    public void Repeating_a_skipped_switch_soon_forces_it()
    {
        var tracker = Tracker();
        Assert.False(tracker.ShouldForce("tv"));

        tracker.RecordSkip("tv");
        _now += TimeSpan.FromSeconds(2);

        Assert.True(tracker.ShouldForce("tv"));
    }

    [Fact]
    public void A_different_profile_or_a_late_repeat_does_not_force()
    {
        var tracker = Tracker();
        tracker.RecordSkip("tv");

        Assert.False(tracker.ShouldForce("desk"));
        _now += ReapplyTracker.Window + TimeSpan.FromSeconds(1);
        Assert.False(tracker.ShouldForce("tv"));
    }

    [Fact]
    public void An_applied_switch_ends_the_window()
    {
        var tracker = Tracker();
        tracker.RecordSkip("tv");

        tracker.Reset();

        Assert.False(tracker.ShouldForce("tv"));
    }
}
