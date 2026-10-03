using DisplaySelector.Core.Activation;
using DisplaySelector.Core.Profiles;
using Xunit;

namespace DisplaySelector.Tests;

public class LiveStateTrackerTests
{
    private static readonly DisplayTarget Tv = new() { StableId = "Hdmi:0", Primary = true };
    private static readonly DisplayTarget Desk = new() { StableId = "Dp:1", Primary = true };

    private static Profile Full(string name, DisplayTarget display, string audio) => new()
    {
        Id = name,
        Name = name,
        Display = new DisplayConfig { Targets = { new DisplayTarget { StableId = display.StableId, Primary = true } } },
        Audio = new AudioConfig { EndpointId = audio },
    };

    private readonly StubAudioService _audio = new();
    private readonly List<Profile> _profiles = new();

    private LiveStateTracker Tracker(ScriptedDisplayService display) =>
        new(display, _audio, new ProfileActivator(display, _audio, new NullLog()), () => _profiles, new NullLog());

    [Fact]
    public void Refresh_reports_a_change_once_and_follows_the_live_hardware()
    {
        _profiles.Add(Full("Desk", Desk, "{speakers}"));
        _profiles.Add(Full("TV", Tv, "{tv}"));
        var display = new ScriptedDisplayService(new[] { Desk }, new[] { Desk }, new[] { Tv }, new[] { Tv });
        var tracker = Tracker(display);
        _audio.DefaultId = "{speakers}";

        Assert.True(tracker.Refresh());
        Assert.Equal("Desk", tracker.ActiveProfileId);
        Assert.False(tracker.Refresh()); // nothing changed

        // Mid-switch to the TV: the picture moved, but its audio device hasn't arrived yet.
        Assert.True(tracker.Refresh());
        Assert.Null(tracker.ActiveProfileId);

        _audio.DefaultId = "{tv}";
        Assert.True(tracker.Refresh());
        Assert.Equal("TV", tracker.ActiveProfileId);
    }

    [Fact]
    public void A_tie_with_no_exactly_live_profile_goes_to_the_first_match()
    {
        _profiles.Add(Full("TV 60Hz", Tv, "{tv}"));
        _profiles.Add(Full("TV 120Hz", Tv, "{tv}"));
        var display = new ScriptedDisplayService(new[] { Tv }) { MatchesResult = false };
        _audio.DefaultId = "{tv}";

        var tracker = Tracker(display);
        tracker.Refresh();

        Assert.Equal("TV 60Hz", tracker.ActiveProfileId); // still marked: a drifted refresh rate isn't "custom"
        Assert.Equal(1, _audio.DefaultQueries); // the tie-break reuses the snapshot's audio reading
    }

    [Fact]
    public void One_refresh_reads_the_hardware_once_for_every_view()
    {
        _audio.DefaultId = "{tv}";
        var display = new ScriptedDisplayService(new[] { Tv }, new[] { Desk });
        var tracker = Tracker(display);

        tracker.Refresh();
        Assert.Equal(0, display.ConnectedQueries); // details are read only when a live window asks

        var snapshot = tracker.Snapshot;
        _ = snapshot.ConnectedDisplays;
        _ = snapshot.ConnectedDisplays;
        _ = snapshot.AudioDevices;
        _ = snapshot.AudioDevices;

        Assert.Equal(new[] { "Hdmi:0" }, snapshot.ActiveDisplays.Select(d => d.StableId));
        Assert.Equal("{tv}", snapshot.DefaultAudioId);
        Assert.Equal(1, display.Calls);
        Assert.Equal(1, display.ConnectedQueries);
        Assert.Equal(1, _audio.DeviceQueries);

        tracker.Refresh(); // a new snapshot: the next layout
        Assert.NotSame(snapshot, tracker.Snapshot);
        Assert.Equal(new[] { "Dp:1" }, tracker.Snapshot.ActiveDisplays.Select(d => d.StableId));
    }

    [Fact]
    public void Audio_only_rolls_up_to_a_matching_full_profile_listed_before_it()
    {
        // Both are exactly live; the tie goes to the first in menu order.
        _profiles.Add(Full("TV", Tv, "{tv}"));
        _profiles.Add(new Profile { Id = "TV sound", Name = "TV sound", Audio = new AudioConfig { EndpointId = "{tv}" } });
        _audio.DefaultId = "{tv}";

        var tracker = Tracker(new ScriptedDisplayService(new[] { Tv }));
        tracker.Refresh();

        Assert.Equal("TV", tracker.ActiveProfileId);
    }

    [Fact]
    public void A_failed_query_keeps_the_previous_answer()
    {
        _profiles.Add(Full("Desk", Desk, "{speakers}"));
        _audio.DefaultId = "{speakers}";
        var tracker = Tracker(new ScriptedDisplayService(new[] { Desk }));
        tracker.Refresh();
        var snapshot = tracker.Snapshot;

        _audio.Throws = true;

        Assert.False(tracker.Refresh());
        Assert.Equal("Desk", tracker.ActiveProfileId);
        Assert.Same(snapshot, tracker.Snapshot); // the windows keep showing the last good reading
    }
}
