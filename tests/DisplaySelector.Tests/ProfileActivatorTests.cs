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
    public void Happy_path_applies_display_sets_audio_and_leaves_the_tone_to_the_confirm_step()
    {
        var display = new ScriptedDisplayService { MatchesResult = false };
        var audio = new FakeAudioService { SetResult = true };

        var result = new ProfileActivator(display, audio, new NullLog()).Activate(FullProfile());

        Assert.True(result.Success);
        Assert.False(result.AlreadyActive);
        Assert.Empty(result.Messages);
        Assert.Equal(1, display.ApplyCount);
        Assert.Equal("{id}", audio.LastSetId);
        Assert.Equal("{id}", result.AudioToConfirm?.EndpointId);
    }

    [Fact]
    public void Audio_set_failure_is_left_to_the_confirm_step()
    {
        // Often just "not there yet" (a TV's HDMI audio arrives after the display switch).
        var display = new ScriptedDisplayService { MatchesResult = false };
        var audio = new FakeAudioService { SetResult = false };

        var result = new ProfileActivator(display, audio, new NullLog()).Activate(FullProfile());

        Assert.True(result.Success);
        Assert.Empty(result.Messages);
        Assert.Equal("{id}", result.AudioToConfirm?.EndpointId);
    }

    [Fact]
    public void Display_failure_is_surfaced()
    {
        var display = new ScriptedDisplayService { MatchesResult = false, ApplyResult = DisplayApplyResult.Fail("nope") };
        var audio = new FakeAudioService { SetResult = true };

        var result = new ProfileActivator(display, audio, new NullLog()).Activate(FullProfile());

        Assert.False(result.Success);
        Assert.Contains(result.Messages, m => m.Contains("nope"));
    }

    [Fact]
    public void Unavailable_displays_are_reported_but_not_a_failure()
    {
        var tv = new DisplayTarget { StableId = "Hdmi:0", Friendly = "LG TV" };
        var display = new ScriptedDisplayService { MatchesResult = false, ApplyResult = DisplayApplyResult.Ok(new[] { tv }) };
        var audio = new FakeAudioService { SetResult = true };

        var result = new ProfileActivator(display, audio, new NullLog()).Activate(FullProfile());

        Assert.True(result.Success);
        Assert.Empty(result.Messages); // the controller words it from UnavailableDisplays
        Assert.Equal("Hdmi:0", Assert.Single(result.UnavailableDisplays).StableId);
    }

    [Fact]
    public void Audio_only_profile_switches_audio_and_skips_display()
    {
        var display = new ScriptedDisplayService { MatchesResult = false };
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
        Assert.Equal("{hp}", result.AudioToConfirm?.EndpointId);
    }

    [Fact]
    public void Display_only_profile_changes_display_and_makes_no_audio_calls()
    {
        var display = new ScriptedDisplayService { MatchesResult = false };
        var audio = new FakeAudioService { SetResult = true };
        var profile = new Profile
        {
            Name = "Desk (display only)",
            Display = new DisplayConfig { PathInfo = "x", ModeInfo = "y", Targets = { new DisplayTarget { StableId = "Dvi:0", Primary = true } } },
            Audio = null,
        };

        var result = new ProfileActivator(display, audio, new NullLog()).Activate(profile);

        Assert.True(result.Success);
        Assert.Equal(1, display.ApplyCount);
        Assert.Null(audio.LastSetId);
        Assert.Null(result.AudioToConfirm);
    }

    [Fact]
    public void Already_live_profile_is_skipped()
    {
        var display = new ScriptedDisplayService();
        var audio = new FakeAudioService();
        audio.SetDefaultOutputDevice("{id}");

        var result = new ProfileActivator(display, audio, new NullLog()).Activate(FullProfile());

        Assert.True(result.AlreadyActive);
        Assert.True(result.Success);
        Assert.Equal(0, display.ApplyCount);
        Assert.Null(result.AudioToConfirm);
    }

    [Fact]
    public void Forced_activation_re_applies_a_live_profile()
    {
        var display = new ScriptedDisplayService();
        var audio = new FakeAudioService();
        audio.SetDefaultOutputDevice("{id}");

        var result = new ProfileActivator(display, audio, new NullLog()).Activate(FullProfile(), force: true);

        Assert.False(result.AlreadyActive);
        Assert.Equal(1, display.ApplyCount);
        Assert.Equal("{id}", result.AudioToConfirm?.EndpointId);
    }

    [Fact]
    public void Profile_is_applied_when_the_layout_differs_in_detail()
    {
        // Same displays and resolutions, but e.g. duplicate vs extend, swapped positions or another refresh rate.
        var display = new ScriptedDisplayService { MatchesResult = false };
        var audio = new FakeAudioService();
        audio.SetDefaultOutputDevice("{id}");

        var result = new ProfileActivator(display, audio, new NullLog()).Activate(FullProfile());

        Assert.False(result.AlreadyActive);
        Assert.Equal(1, display.ApplyCount);
    }

    [Fact]
    public void Audio_only_profile_is_live_on_its_device()
    {
        var audio = new FakeAudioService();
        audio.SetDefaultOutputDevice("{hs}");
        var activator = new ProfileActivator(new ScriptedDisplayService(), audio, new NullLog());

        Assert.True(activator.IsLive(new Profile { Audio = new AudioConfig { EndpointId = "{hs}" } }));
        Assert.False(activator.IsLive(new Profile { Audio = new AudioConfig { EndpointId = "{tv}" } }));
    }

    [Fact]
    public void A_profile_with_nothing_saved_is_never_live()
    {
        var activator = new ProfileActivator(new ScriptedDisplayService(), new FakeAudioService(), new NullLog());

        Assert.False(activator.IsLive(new Profile()));
    }

    [Fact]
    public void Profile_is_applied_when_only_the_audio_differs()
    {
        var display = new ScriptedDisplayService();
        var audio = new FakeAudioService();
        audio.SetDefaultOutputDevice("{speakers}");

        var result = new ProfileActivator(display, audio, new NullLog()).Activate(FullProfile());

        Assert.False(result.AlreadyActive);
        Assert.Equal(1, display.ApplyCount);
    }

    private sealed class FakeAudioService : IAudioService
    {
        private AudioEndpoint? _current;

        public bool SetResult { get; set; } = true;

        public string? LastSetId { get; private set; }


        public IReadOnlyList<AudioEndpoint> GetOutputDevices() => Array.Empty<AudioEndpoint>();

        public AudioEndpoint? GetDefaultOutputDevice() => _current;

        public bool IsDeviceActive(string endpointId) => true;

        public bool SetDefaultOutputDevice(string endpointId)
        {
            LastSetId = endpointId;
            if (SetResult)
            {
                _current = new AudioEndpoint(endpointId, "device", true);
            }

            return SetResult;
        }

        public Task PlayConfirmationAsync(string? endpointId = null) => throw new InvalidOperationException("the activator never plays the tone");

        public event Action? DefaultDeviceChanged { add { } remove { } }
    }
}
