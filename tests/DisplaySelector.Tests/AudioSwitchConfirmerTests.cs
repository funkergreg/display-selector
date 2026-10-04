using DisplaySelector.Core.Activation;
using DisplaySelector.Core.Audio;
using DisplaySelector.Core.Profiles;
using Xunit;

namespace DisplaySelector.Tests;

public class AudioSwitchConfirmerTests
{
    private const string Soundbar = "{soundbar}";
    private const string Speakers = "{speakers}";

    private static AudioSwitchConfirmer Confirmer(IAudioService audio, int polls = 8) =>
        new(
            audio,
            new NullLog(),
            (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            },
            pollInterval: TimeSpan.FromMilliseconds(250),
            window: TimeSpan.FromMilliseconds(250 * polls));

    private static AudioEndpoint Device(string id, bool isDefault) => new(id, id, isDefault);

    private static AudioConfig Audio(string id) => new() { EndpointId = id, FriendlyName = id };

    [Fact]
    public async Task Plays_the_tone_once_the_device_is_the_stable_default()
    {
        var audio = new ScriptedAudioService(new[] { Device(Soundbar, true) });

        var outcome = await Confirmer(audio).ConfirmAsync(Audio(Soundbar), CancellationToken.None);

        Assert.Equal(AudioConfirmOutcome.Confirmed, outcome);
        Assert.Equal(new[] { Soundbar }, audio.Tones); // exactly once, on the profile's device
        Assert.Empty(audio.SetCalls);
    }

    [Fact]
    public async Task Waits_for_a_device_that_arrives_after_the_display_switch()
    {
        // The TV's HDMI audio isn't there for the first polls, then appears as the default.
        var audio = new ScriptedAudioService(
            new[] { Device(Speakers, true) },
            new[] { Device(Speakers, true) },
            new[] { Device(Speakers, false), Device(Soundbar, true) });

        var outcome = await Confirmer(audio).ConfirmAsync(Audio(Soundbar), CancellationToken.None);

        Assert.Equal(AudioConfirmOutcome.Confirmed, outcome);
        Assert.Equal(new[] { Soundbar }, audio.Tones);
    }

    [Fact]
    public async Task Sets_the_default_back_when_windows_takes_it_away()
    {
        // The profile wants the speakers; the TV's audio arrives and Windows makes it the default.
        var audio = new ScriptedAudioService(
            new[] { Device(Speakers, true) },
            new[] { Device(Speakers, true) },
            new[] { Device(Speakers, false), Device(Soundbar, true) });

        var outcome = await Confirmer(audio).ConfirmAsync(Audio(Speakers), CancellationToken.None);

        Assert.Equal(AudioConfirmOutcome.Confirmed, outcome);
        Assert.Equal(new[] { Speakers }, audio.Tones);
        Assert.InRange(audio.SetCalls.Count, 1, AudioSwitchConfirmer.MaxReasserts);
        Assert.All(audio.SetCalls, id => Assert.Equal(Speakers, id));
    }

    [Fact]
    public async Task Gives_up_re_asserting_after_the_limit()
    {
        var audio = new ScriptedAudioService(new[] { Device(Speakers, false), Device(Soundbar, true) })
        {
            SetTakesEffect = false, // something keeps the other device as default
        };

        var outcome = await Confirmer(audio, polls: 20).ConfirmAsync(Audio(Speakers), CancellationToken.None);

        Assert.Equal(AudioConfirmOutcome.NotDefault, outcome);
        Assert.Equal(AudioSwitchConfirmer.MaxReasserts, audio.SetCalls.Count);
        Assert.Empty(audio.Tones);
    }

    [Fact]
    public async Task Device_that_never_arrives_is_not_available_and_plays_nothing()
    {
        var audio = new ScriptedAudioService(new[] { Device(Speakers, true) });

        var outcome = await Confirmer(audio).ConfirmAsync(Audio(Soundbar), CancellationToken.None);

        Assert.Equal(AudioConfirmOutcome.NotAvailable, outcome);
        Assert.Empty(audio.Tones);
        Assert.Empty(audio.SetCalls);
    }

    [Fact]
    public async Task A_failed_query_counts_as_not_ready_yet()
    {
        var audio = new ScriptedAudioService(new[] { Device(Soundbar, true) }) { ThrowOnCalls = { 1, 2 } };

        var outcome = await Confirmer(audio).ConfirmAsync(Audio(Soundbar), CancellationToken.None);

        Assert.Equal(AudioConfirmOutcome.Confirmed, outcome);
    }

    [Fact]
    public async Task Cancellation_stops_before_the_tone()
    {
        var audio = new ScriptedAudioService(new[] { Device(Soundbar, true) });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Confirmer(audio).ConfirmAsync(Audio(Soundbar), cancellation.Token));
        Assert.Empty(audio.Tones);
    }

    /// <summary>Returns a scripted sequence of device lists (the last repeats); set calls make that device the default.</summary>
    private sealed class ScriptedAudioService : IAudioService
    {
        private readonly AudioEndpoint[][] _script;
        private string? _forcedDefault;
        private int _calls;

        public ScriptedAudioService(params AudioEndpoint[][] script)
        {
            _script = script;
        }

        public bool SetTakesEffect { get; init; } = true;

        public HashSet<int> ThrowOnCalls { get; } = new();

        public List<string> SetCalls { get; } = new();

        public IReadOnlyList<AudioEndpoint> GetOutputDevices()
        {
            _calls++;
            if (ThrowOnCalls.Contains(_calls))
            {
                throw new InvalidOperationException("device arriving");
            }

            var devices = _script[Math.Min(_calls - 1, _script.Length - 1)];
            return _forcedDefault is null || devices.All(d => d.Id != _forcedDefault)
                ? devices
                : devices.Select(d => d with { IsDefault = d.Id == _forcedDefault }).ToArray();
        }

        private IReadOnlyList<AudioEndpoint> _snapshot = Array.Empty<AudioEndpoint>();

        // One poll = IsDeviceActive (advances the script) then GetDefaultOutputDevice (same snapshot).
        public bool IsDeviceActive(string endpointId)
        {
            _snapshot = GetOutputDevices();
            return _snapshot.Any(d => d.Id == endpointId);
        }

        public AudioEndpoint? GetDefaultOutputDevice() => _snapshot.FirstOrDefault(d => d.IsDefault);

        public bool SetDefaultOutputDevice(string endpointId)
        {
            SetCalls.Add(endpointId);
            if (SetTakesEffect)
            {
                _forcedDefault = endpointId;
            }
            return true;
        }

        public List<string?> Tones { get; } = new();

        public Task PlayConfirmationAsync(string? endpointId = null)
        {
            Tones.Add(endpointId);
            return Task.CompletedTask;
        }

        public event Action? DefaultDeviceChanged { add { } remove { } }
    }
}
