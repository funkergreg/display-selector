using DisplaySelector.Core.Activation;
using DisplaySelector.Core.Display;
using DisplaySelector.Core.Launch;
using DisplaySelector.Core.Profiles;
using Xunit;

namespace DisplaySelector.Tests;

public class LaunchCoordinatorTests
{
    private static readonly DisplayTarget TvLive = new() { StableId = "Hdmi:0", Primary = true };

    private static readonly Profile Tv = new()
    {
        Id = "tv-id",
        Name = "TV",
        Display = new DisplayConfig { Targets = { new DisplayTarget { StableId = "Hdmi:0", Primary = true } } },
    };

    private static readonly Profile Headset = new()
    {
        Id = "hs-id",
        Name = "Headset",
        Audio = new AudioConfig { EndpointId = "{hs}" },
    };

    private readonly List<string> _events = new();
    private readonly ScriptedDisplayService _display = new(new[] { TvLive });

    private LaunchCoordinator Coordinator(FakeLauncher launcher, ScriptedDisplayService? display = null) =>
        new(
            launcher,
            new DisplaySettleWaiter(
                display ?? _display,
                new NullLog(),
                (_, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    _events.Add("poll");
                    return Task.CompletedTask;
                },
                timeout: TimeSpan.FromSeconds(1)),
            new NullLog());

    private Func<Profile, ActivationResult?> Activate(bool success = true) =>
        profile =>
        {
            _events.Add($"activate:{profile.Name}");
            return new ActivationResult(success, Array.Empty<string>());
        };

    [Fact]
    public async Task Activates_then_waits_for_displays_then_launches()
    {
        var launcher = new FakeLauncher(_events);

        var outcome = await Coordinator(launcher).RunAsync(
            new LaunchCommand("tv-id", "game.exe", "-x"), new[] { Tv }, Activate(), CancellationToken.None);

        Assert.True(outcome.Launched);
        Assert.Same(Tv, outcome.Profile);
        Assert.Equal(SettleOutcome.Settled, outcome.Settle.Outcome);
        Assert.Equal("activate:TV", _events[0]);
        Assert.Equal("launch:game.exe -x", _events[^1]);
        Assert.Contains("poll", _events);
    }

    [Fact]
    public async Task Missing_profile_still_launches_without_switching()
    {
        var launcher = new FakeLauncher(_events);

        var outcome = await Coordinator(launcher).RunAsync(
            new LaunchCommand("deleted", "game.exe"), new[] { Tv }, Activate(), CancellationToken.None);

        Assert.Null(outcome.Profile);
        Assert.True(outcome.Launched);
        Assert.Equal(new[] { "launch:game.exe " }, _events);
    }

    [Fact]
    public async Task Switch_only_command_activates_and_never_waits_or_launches()
    {
        var launcher = new FakeLauncher(_events);

        var outcome = await Coordinator(launcher).RunAsync(
            new LaunchCommand("TV"), new[] { Tv }, Activate(), CancellationToken.None);

        Assert.False(outcome.LaunchAttempted);
        Assert.Equal(new[] { "activate:TV" }, _events);
    }

    [Fact]
    public async Task Already_live_profile_launches_at_once_without_waiting()
    {
        var launcher = new FakeLauncher(_events);
        Func<Profile, ActivationResult?> live = profile =>
        {
            _events.Add($"activate:{profile.Name}");
            return ActivationResult.Live;
        };

        var outcome = await Coordinator(launcher).RunAsync(
            new LaunchCommand("tv-id", "game.exe"), new[] { Tv }, live, CancellationToken.None);

        Assert.True(outcome.Launched);
        Assert.True(outcome.Activation!.AlreadyActive);
        Assert.Equal(SettleOutcome.Skipped, outcome.Settle.Outcome);
        Assert.Equal(new[] { "activate:TV", "launch:game.exe " }, _events);
    }

    [Fact]
    public async Task Audio_only_profile_skips_the_settle_wait()
    {
        var launcher = new FakeLauncher(_events);

        var outcome = await Coordinator(launcher).RunAsync(
            new LaunchCommand("hs-id", "game.exe"), new[] { Headset }, Activate(), CancellationToken.None);

        Assert.Equal(SettleOutcome.Skipped, outcome.Settle.Outcome);
        Assert.Equal(new[] { "activate:Headset", "launch:game.exe " }, _events);
    }

    [Fact]
    public async Task Failed_display_apply_skips_the_wait_but_still_launches()
    {
        var launcher = new FakeLauncher(_events);

        var outcome = await Coordinator(launcher).RunAsync(
            new LaunchCommand("tv-id", "game.exe"), new[] { Tv }, Activate(success: false), CancellationToken.None);

        Assert.Equal(SettleOutcome.Skipped, outcome.Settle.Outcome);
        Assert.True(outcome.Launched);
    }

    [Fact]
    public async Task Settle_timeout_still_launches()
    {
        var neverMatches = new ScriptedDisplayService(new[] { new DisplayTarget { StableId = "Dp:1", Primary = true } });
        var launcher = new FakeLauncher(_events);

        var outcome = await Coordinator(launcher, neverMatches).RunAsync(
            new LaunchCommand("tv-id", "game.exe"), new[] { Tv }, Activate(), CancellationToken.None);

        Assert.Equal(SettleOutcome.TimedOut, outcome.Settle.Outcome);
        Assert.True(outcome.Launched);
    }

    private static readonly DisplayTarget TvOff = new() { StableId = "Hdmi:0", Friendly = "LG TV" };

    // Activation results in order (the last repeats), modelling the TV's state at each activation.
    private Func<Profile, ActivationResult?> ActivateSequence(params ActivationResult[] results)
    {
        var calls = 0;
        return profile =>
        {
            _events.Add($"activate:{profile.Name}");
            return results[Math.Min(calls++, results.Length - 1)];
        };
    }

    private static ActivationResult Applied(params DisplayTarget[] unavailable) =>
        new(true, Array.Empty<string>()) { UnavailableDisplays = unavailable };

    [Fact]
    public async Task Tv_turned_on_during_the_wait_reactivates_the_profile_before_launching()
    {
        _display.Connects = (2, "Hdmi:0");
        var launcher = new FakeLauncher(_events);

        var outcome = await Coordinator(launcher).RunAsync(
            new LaunchCommand("tv-id", "game.exe"), new[] { Tv }, ActivateSequence(Applied(TvOff), Applied()), CancellationToken.None);

        Assert.Equal(2, _events.Count(e => e == "activate:TV"));
        Assert.Equal("Hdmi:0", Assert.Single(outcome.ReconnectedDisplays).StableId);
        Assert.Equal(SettleOutcome.Settled, outcome.Settle.Outcome);
        Assert.Empty(outcome.Activation!.UnavailableDisplays);
        Assert.Equal("launch:game.exe ", _events[^1]);
    }

    [Fact]
    public async Task Reconnect_that_finds_the_profile_already_live_reports_it_applied()
    {
        _display.Connects = (2, "Hdmi:0");
        var launcher = new FakeLauncher(_events);

        var outcome = await Coordinator(launcher).RunAsync(
            new LaunchCommand("tv-id", "game.exe"), new[] { Tv }, ActivateSequence(Applied(TvOff), ActivationResult.Live), CancellationToken.None);

        // A switch did happen, and the TV is no longer missing: not "already on", not "not available".
        Assert.False(outcome.Activation!.AlreadyActive);
        Assert.True(outcome.Activation.Success);
        Assert.Empty(outcome.Activation.UnavailableDisplays);
        Assert.Empty(outcome.Activation.Messages);
        Assert.Equal("Hdmi:0", Assert.Single(outcome.ReconnectedDisplays).StableId);
        Assert.True(outcome.Launched);
    }

    [Fact]
    public async Task Tv_never_turned_on_waits_out_the_timeout_then_launches()
    {
        var deskOnly = new ScriptedDisplayService(new[] { new DisplayTarget { StableId = "Dp:1", Primary = true } });
        var launcher = new FakeLauncher(_events);

        var outcome = await Coordinator(launcher, deskOnly).RunAsync(
            new LaunchCommand("tv-id", "game.exe"), new[] { Tv }, ActivateSequence(Applied(TvOff)), CancellationToken.None);

        Assert.Equal(1, _events.Count(e => e == "activate:TV"));
        Assert.Equal(SettleOutcome.TimedOut, outcome.Settle.Outcome);
        Assert.Empty(outcome.ReconnectedDisplays);
        Assert.True(outcome.Launched);
    }

    [Fact]
    public async Task Failed_apply_with_a_missing_display_still_waits_for_it()
    {
        _display.Connects = (1, "Hdmi:0");
        var failed = new ActivationResult(false, Array.Empty<string>()) { UnavailableDisplays = new[] { TvOff } };
        var launcher = new FakeLauncher(_events);

        var outcome = await Coordinator(launcher).RunAsync(
            new LaunchCommand("tv-id", "game.exe"), new[] { Tv }, ActivateSequence(failed, Applied()), CancellationToken.None);

        Assert.Equal(2, _events.Count(e => e == "activate:TV"));
        Assert.True(outcome.Activation!.Success);
        Assert.True(outcome.Launched);
    }

    [Fact]
    public async Task A_display_triggers_reactivation_only_once()
    {
        // Flapping HDMI: connected-query says it's there, yet the re-activation still finds it missing.
        _display.Connects = (1, "Hdmi:0");
        var launcher = new FakeLauncher(_events);

        var outcome = await Coordinator(launcher).RunAsync(
            new LaunchCommand("tv-id", "game.exe"), new[] { Tv }, ActivateSequence(Applied(TvOff)), CancellationToken.None);

        Assert.Equal(2, _events.Count(e => e == "activate:TV"));
        Assert.True(outcome.Launched);
    }

    [Fact]
    public async Task Launch_failure_is_reported()
    {
        var launcher = new FakeLauncher(_events) { Error = "file not found" };

        var outcome = await Coordinator(launcher).RunAsync(
            new LaunchCommand("tv-id", "missing.exe"), new[] { Tv }, Activate(), CancellationToken.None);

        Assert.True(outcome.LaunchAttempted);
        Assert.False(outcome.Launched);
        Assert.Equal("file not found", outcome.LaunchError);
    }

    [Fact]
    public async Task Cancelled_command_does_not_launch()
    {
        var launcher = new FakeLauncher(_events);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Coordinator(launcher).RunAsync(
            new LaunchCommand("tv-id", "game.exe"), new[] { Tv }, Activate(), cancellation.Token));

        Assert.DoesNotContain(_events, e => e.StartsWith("launch:"));
    }

    private sealed class FakeLauncher : IProcessLauncher
    {
        private readonly List<string> _events;

        public FakeLauncher(List<string> events)
        {
            _events = events;
        }

        public string? Error { get; init; }

        public bool TryLaunch(string target, string? arguments, out string? error)
        {
            _events.Add($"launch:{target} {arguments}");
            error = Error;
            return Error is null;
        }
    }
}
