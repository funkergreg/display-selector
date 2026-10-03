using DisplaySelector.Core.Audio;
using DisplaySelector.Core.Display;
using DisplaySelector.Core.Logging;
using DisplaySelector.Core.Profiles;

namespace DisplaySelector.Core.Activation;

/// <summary>Outcome of activating a profile; <see cref="Messages"/> are surfaced to the user.</summary>
public sealed record ActivationResult(bool Success, IReadOnlyList<string> Messages)
{
    /// <summary>
    /// Profile displays that weren't connected at activation (e.g. a TV still powered off). Reported, not
    /// a failure; the caller words it for the user.
    /// </summary>
    public IReadOnlyList<DisplayTarget> UnavailableDisplays { get; init; } = Array.Empty<DisplayTarget>();

    /// <summary>
    /// The profile's full saved state was already live, so nothing was applied (no blackout, no tone).
    /// </summary>
    public bool AlreadyActive { get; init; }

    /// <summary>
    /// The audio device that was set as the default and still needs confirming (it may still be
    /// arriving after the display switch). The caller runs <see cref="AudioSwitchConfirmer"/>, which
    /// plays the tone once it's ready. Null when the profile has no audio or nothing was applied.
    /// </summary>
    public AudioConfig? AudioToConfirm { get; init; }

    public static ActivationResult Live { get; } = new(true, Array.Empty<string>()) { AlreadyActive = true };
}

/// <summary>
/// Orchestrates profile activation, WinForms-free so it is unit-testable with mocked services.
/// Sequence (best-effort, failures surfaced + logged): skip if already live (unless forced) → apply
/// display → set audio (all roles). The confirmation tone is not played here: the device may still be
/// arriving after the display switch, so the caller confirms it asynchronously
/// (<see cref="ActivationResult.AudioToConfirm"/>). Forcing a re-apply of a live profile is the
/// "unstick a frozen Windows display" fix.
/// </summary>
public sealed class ProfileActivator
{
    private readonly IDisplayService _display;
    private readonly IAudioService _audio;
    private readonly ILog _log;

    public ProfileActivator(IDisplayService display, IAudioService audio, ILog log)
    {
        _display = display;
        _audio = audio;
        _log = log;
    }

    /// <param name="profile">The profile to switch to.</param>
    /// <param name="force">Re-apply even when the profile is already live.</param>
    public ActivationResult Activate(Profile profile, bool force = false)
    {
        if (!force && IsLive(profile))
        {
            _log.Info($"Profile '{profile.Name}' is already live; nothing to apply.");
            return ActivationResult.Live;
        }

        _log.Info($"Activating profile '{profile.Name}' (id={profile.Id}){(force ? " (forced re-apply)" : string.Empty)}.");
        var messages = new List<string>();
        var success = true;
        IReadOnlyList<DisplayTarget> unavailableDisplays = Array.Empty<DisplayTarget>();
        AudioConfig? audioToConfirm = null;

        if (profile.Display is { } display)
        {
            var result = _display.Apply(display);
            unavailableDisplays = result.UnavailableTargets;
            if (!result.Success)
            {
                success = false;
                messages.Add($"Display change failed: {result.Error}");
            }
        }

        if (profile.Audio is { } audio && !string.IsNullOrEmpty(audio.EndpointId))
        {
            // A failure here is often just "not there yet" (a TV's HDMI audio arrives after the display
            // switch); the confirm step retries and reports only if the device never becomes ready.
            if (!_audio.SetDefaultOutputDevice(audio.EndpointId))
            {
                _log.Info($"Audio device '{audio.FriendlyName}' couldn't be selected yet; will confirm once it's ready.");
            }
            audioToConfirm = audio;
        }

        _log.Info($"Activation of '{profile.Name}' complete: success={success}. {string.Join("; ", messages)}");
        return new ActivationResult(success, messages)
        {
            UnavailableDisplays = unavailableDisplays,
            AudioToConfirm = audioToConfirm,
        };
    }

    /// <summary>
    /// Whether everything the profile saved is in effect right now: the default audio device, and the
    /// display layout in full detail (<see cref="IDisplayService.MatchesCurrent"/>: displays, positions,
    /// duplicate/extend, resolution, rotation, refresh rate). Unlike the menu's coarse check mark this
    /// asks about one specific profile, so profiles differing only in such details are told apart.
    /// A profile with nothing saved is never live.
    /// </summary>
    public bool IsLive(Profile profile)
    {
        var audioId = profile.Audio?.EndpointId;
        if (profile.Display is null && string.IsNullOrEmpty(audioId))
        {
            return false;
        }

        try
        {
            return (string.IsNullOrEmpty(audioId) || _audio.GetDefaultOutputDevice()?.Id == audioId) &&
                   (profile.Display is null || _display.MatchesCurrent(profile.Display));
        }
        catch (Exception ex)
        {
            // When in doubt, apply: a needless switch beats a skipped one.
            _log.Debug($"Live-state check for '{profile.Name}' failed ({ex.Message}); applying.");
            return false;
        }
    }
}
