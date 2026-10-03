using DisplaySelector.Core.Audio;
using DisplaySelector.Core.Display;
using DisplaySelector.Core.Profiles;

namespace DisplaySelector.Core.Activation;

/// <summary>
/// The live hardware as one <see cref="LiveStateTracker.Refresh"/> read it, shared by everything that shows
/// live state (the check mark, the Display Tester, Profile Diagnostics), so they agree with each other and
/// one refresh queries Windows once. The active displays and the default audio device are read up front
/// (the check mark needs them). The connected displays and the audio device list are read on first use
/// (only the open live windows need them, so an idle refresh costs no more) and then kept.
/// </summary>
public sealed class LiveSnapshot
{
    private readonly IDisplayService? _display;
    private readonly IAudioService? _audio;
    private IReadOnlyList<DisplayTarget>? _connectedDisplays;
    private IReadOnlyList<AudioEndpoint>? _audioDevices;

    /// <summary>Nothing read yet (before the first successful refresh).</summary>
    public static LiveSnapshot Empty { get; } = new();

    private LiveSnapshot()
    {
        ActiveDisplays = Array.Empty<DisplayTarget>();
    }

    /// <summary>Reads the active displays and the default audio device now; may throw (a device mid-arrival).</summary>
    public LiveSnapshot(IDisplayService display, IAudioService audio)
    {
        _display = display;
        _audio = audio;
        DefaultAudioId = audio.GetDefaultOutputDevice()?.Id;
        ActiveDisplays = display.GetCurrentDisplays();
    }

    public string? DefaultAudioId { get; }

    public IReadOnlyList<DisplayTarget> ActiveDisplays { get; }

    /// <summary>Every connected display, in use or not (read on first use).</summary>
    public IReadOnlyList<DisplayTarget> ConnectedDisplays =>
        _connectedDisplays ??= _display?.GetConnectedDisplays() ?? Array.Empty<DisplayTarget>();

    /// <summary>Every audio output device (read on first use).</summary>
    public IReadOnlyList<AudioEndpoint> AudioDevices =>
        _audioDevices ??= _audio?.GetOutputDevices() ?? Array.Empty<AudioEndpoint>();
}
