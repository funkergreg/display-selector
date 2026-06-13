using DisplaySelector.Core.Activation;
using DisplaySelector.Core.Audio;
using DisplaySelector.Core.Display;
using DisplaySelector.Core.Profiles;
using Xunit;

namespace DisplaySelector.Tests;

public class ProfileActivatorTests
{
    private static Profile FullProfile() => new()
    {
        Name = "Test",
        Display = new DisplayConfig { PathInfo = "x", ModeInfo = "y", Targets = { new DisplayTarget { StableId = "Hdmi:0" } } },
        Audio = new AudioConfig { EndpointId = "{id}", FriendlyName = "Soundbar" },
    };

    [Fact]
    public void Happy_path_applies_display_sets_audio_and_plays_tone()
    {
        var display = new FakeDisplayService { ApplyResult = DisplayApplyResult.Ok() };
        var audio = new FakeAudioService { SetResult = true };

        var result = new ProfileActivator(display, audio, new NullLog()).Activate(FullProfile());

        Assert.True(result.Success);
        Assert.Empty(result.Messages);
        Assert.Equal(1, display.ApplyCount);
        Assert.Equal("{id}", audio.LastSetId);
        Assert.Equal("{id}", audio.LastPlayedId);
    }

    [Fact]
    public void Audio_failure_is_surfaced_and_tone_is_not_played()
    {
        var display = new FakeDisplayService { ApplyResult = DisplayApplyResult.Ok() };
        var audio = new FakeAudioService { SetResult = false };

        var result = new ProfileActivator(display, audio, new NullLog()).Activate(FullProfile());

        Assert.False(result.Success);
        Assert.Contains(result.Messages, m => m.Contains("Soundbar"));
        Assert.Null(audio.LastPlayedId);
    }

    [Fact]
    public void Display_failure_is_surfaced()
    {
        var display = new FakeDisplayService { ApplyResult = DisplayApplyResult.Fail("nope") };
        var audio = new FakeAudioService { SetResult = true };

        var result = new ProfileActivator(display, audio, new NullLog()).Activate(FullProfile());

        Assert.False(result.Success);
        Assert.Contains(result.Messages, m => m.Contains("nope"));
    }

    [Fact]
    public void Unavailable_displays_are_reported_but_not_a_failure()
    {
        var display = new FakeDisplayService { ApplyResult = DisplayApplyResult.Ok(new[] { "LG TV" }) };
        var audio = new FakeAudioService { SetResult = true };

        var result = new ProfileActivator(display, audio, new NullLog()).Activate(FullProfile());

        Assert.True(result.Success);
        Assert.Contains(result.Messages, m => m.Contains("LG TV"));
    }

    [Fact]
    public void Audio_set_but_not_active_reports_message_and_skips_tone()
    {
        // Models the powered-off TV/soundbar: the set call "succeeds" but the endpoint isn't actually active.
        var display = new FakeDisplayService { ApplyResult = DisplayApplyResult.Ok() };
        var audio = new FakeAudioService { SetResult = true, SimulateAvailable = false };

        var result = new ProfileActivator(display, audio, new NullLog()).Activate(FullProfile());

        Assert.True(result.Success); // best-effort: kept set, will apply when ready
        Assert.Contains(result.Messages, m => m.Contains("isn't available yet"));
        Assert.Null(audio.LastPlayedId); // must not play the tone on the old device
    }

    [Fact]
    public void Audio_only_profile_switches_audio_and_skips_display()
    {
        var display = new FakeDisplayService { ApplyResult = DisplayApplyResult.Ok() };
        var audio = new FakeAudioService { SetResult = true };
        var profile = new Profile
        {
            Name = "Headset",
            Display = null, // audio-only
            Audio = new AudioConfig { EndpointId = "{hp}", FriendlyName = "Headset" },
        };

        var result = new ProfileActivator(display, audio, new NullLog()).Activate(profile);

        Assert.True(result.Success);
        Assert.Equal(0, display.ApplyCount);
        Assert.Equal("{hp}", audio.LastSetId);
        Assert.Equal("{hp}", audio.LastPlayedId);
    }

    [Fact]
    public void Display_only_profile_changes_display_and_makes_no_audio_calls()
    {
        var display = new FakeDisplayService { ApplyResult = DisplayApplyResult.Ok() };
        var audio = new FakeAudioService { SetResult = true };
        var profile = new Profile
        {
            Name = "Desk (display only)",
            Display = new DisplayConfig { PathInfo = "x", ModeInfo = "y" },
            Audio = null,
        };

        var result = new ProfileActivator(display, audio, new NullLog()).Activate(profile);

        Assert.True(result.Success);
        Assert.Equal(1, display.ApplyCount);
        Assert.Null(audio.LastSetId);
        Assert.Null(audio.LastPlayedId);
    }

    private sealed class FakeDisplayService : IDisplayService
    {
        public DisplayApplyResult ApplyResult { get; set; } = DisplayApplyResult.Ok();

        public int ApplyCount { get; private set; }

        public DisplayConfig Capture() => new();

        public IReadOnlyList<DisplayTarget> GetCurrentDisplays() => Array.Empty<DisplayTarget>();

        public bool ValidateCurrent() => true;

        public DisplayApplyResult ReapplyCurrent() => DisplayApplyResult.Ok();

        public DisplayApplyResult Apply(DisplayConfig config)
        {
            ApplyCount++;
            return ApplyResult;
        }
    }

    private sealed class FakeAudioService : IAudioService
    {
        private AudioEndpoint? _current;

        public bool SetResult { get; set; } = true;

        /// <summary>When false, a successful set does NOT become the active default (device powered off).</summary>
        public bool SimulateAvailable { get; set; } = true;

        public string? LastSetId { get; private set; }

        public string? LastPlayedId { get; private set; }

        public IReadOnlyList<AudioEndpoint> GetOutputDevices() => Array.Empty<AudioEndpoint>();

        public AudioEndpoint? GetDefaultOutputDevice() => _current;

        public bool SetDefaultOutputDevice(string endpointId)
        {
            LastSetId = endpointId;
            if (SetResult && SimulateAvailable)
            {
                _current = new AudioEndpoint(endpointId, "device", true);
            }

            return SetResult;
        }

        public void PlayConfirmation(string? endpointId = null) => LastPlayedId = endpointId;
    }
}
