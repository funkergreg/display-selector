using System.Diagnostics;
using DisplaySelector.Core.Audio;
using DisplaySelector.Core.Logging;
using DisplaySelector.Core.Profiles;

namespace DisplaySelector.Core.Activation;

public enum AudioConfirmOutcome
{
    /// <summary>The device became (and stayed) the default; the tone was played on it.</summary>
    Confirmed,

    /// <summary>The device never became active within the window (e.g. a soundbar that's off).</summary>
    NotAvailable,

    /// <summary>The device was active, but something kept taking the default back.</summary>
    NotDefault,
}

/// <summary>
/// Confirms an audio switch after activation, then plays the confirmation tone on the new device.
/// <para>
/// A display switch reshapes the audio devices too: a TV's HDMI audio endpoint only exists while a
/// display path to the TV is active, so right after <c>SetDisplayConfig</c> it is still arriving.
/// Playing the tone on it straight away can stall (the hang in DESIGN.md §11), and when it does arrive
/// Windows may make it the default by itself, undoing the profile's choice. So this polls: once the
/// profile's device is active and the default for <see cref="StablePolls"/> polls in a row, the tone
/// plays (off the UI thread). For the rest of the window, if the default moves away from an active
/// device it is set back, at most <see cref="MaxReasserts"/> times so it can't fight the user.
/// </para>
/// Delay is injected so tests run instantly; cancellation means a newer activation took over.
/// </summary>
public sealed class AudioSwitchConfirmer
{
    public const int StablePolls = 2;
    public const int MaxReasserts = 3;

    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(10);

    private readonly IAudioService _audio;
    private readonly ILog _log;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TimeSpan _pollInterval;
    private readonly int _maxPolls;

    public AudioSwitchConfirmer(
        IAudioService audio,
        ILog log,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        TimeSpan? pollInterval = null,
        TimeSpan? window = null)
    {
        _audio = audio;
        _log = log;
        _delay = delay ?? Task.Delay;
        _pollInterval = pollInterval ?? DefaultPollInterval;
        _maxPolls = Math.Max(1, (int)Math.Ceiling((window ?? DefaultWindow) / _pollInterval));
    }

    public async Task<AudioConfirmOutcome> ConfirmAsync(AudioConfig audio, CancellationToken cancellationToken)
    {
        var endpointId = audio.EndpointId;
        var stopwatch = Stopwatch.StartNew();
        var streak = 0;
        var reasserts = 0;
        var toned = false;
        var everActive = false;

        for (var poll = 1; poll <= _maxPolls; poll++)
        {
            await _delay(_pollInterval, cancellationToken);

            // Two light queries (this endpoint's state, the default's id) rather than a full enumeration.
            bool active;
            bool isDefault;
            try
            {
                active = _audio.IsDeviceActive(endpointId);
                isDefault = active && _audio.GetDefaultOutputDeviceId() == endpointId;
            }
            catch (Exception ex)
            {
                // A query can race a device arriving or leaving; treat it as "not yet".
                _log.Debug($"Audio confirm poll {poll}: device query failed ({ex.Message}).");
                streak = 0;
                continue;
            }

            everActive |= active;
            if (active && !isDefault)
            {
                streak = 0;
                if (reasserts >= MaxReasserts)
                {
                    continue;
                }

                reasserts++;
                _log.Info($"Audio confirm: '{audio.FriendlyName}' is active but not the default; setting it again ({reasserts}/{MaxReasserts}).");
                _audio.SetDefaultOutputDevice(endpointId);
                continue;
            }

            streak = active ? streak + 1 : 0;
            _log.Debug($"Audio confirm poll {poll}: active={active} stable={streak}");
            if (!toned && streak >= StablePolls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                toned = true;
                _log.Info($"Audio device '{audio.FriendlyName}' ready after {poll} poll(s), {stopwatch.ElapsedMilliseconds} ms.");
                await _audio.PlayConfirmationAsync(endpointId);
            }
        }

        if (toned)
        {
            return AudioConfirmOutcome.Confirmed;
        }

        var outcome = everActive ? AudioConfirmOutcome.NotDefault : AudioConfirmOutcome.NotAvailable;
        _log.Info($"Audio device '{audio.FriendlyName}' not confirmed within {stopwatch.ElapsedMilliseconds} ms: {outcome}.");
        return outcome;
    }
}
