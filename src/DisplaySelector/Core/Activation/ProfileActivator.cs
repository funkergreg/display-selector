using DisplaySelector.Core.Audio;
using DisplaySelector.Core.Display;
using DisplaySelector.Core.Logging;
using DisplaySelector.Core.Profiles;

namespace DisplaySelector.Core.Activation;

/// <summary>Outcome of activating a profile; <see cref="Messages"/> are surfaced to the user.</summary>
public sealed record ActivationResult(bool Success, IReadOnlyList<string> Messages);

/// <summary>
/// Orchestrates profile activation, WinForms-free so it is unit-testable with mocked services.
/// Sequence (best-effort, failures surfaced + logged): apply display → set audio (all roles) →
/// confirmation tone on the new device. Re-applying the active profile is intentional (the unstick fix).
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

    public ActivationResult Activate(Profile profile)
    {
        _log.Info($"Activating profile '{profile.Name}' (id={profile.Id}).");
        var messages = new List<string>();
        var success = true;

        if (profile.Display is { } display)
        {
            var result = _display.Apply(display);
            if (!result.Success)
            {
                success = false;
                messages.Add($"Display change failed: {result.Error}");
            }
            else if (result.UnavailableTargets.Count > 0)
            {
                messages.Add($"Displays not available: {string.Join(", ", result.UnavailableTargets)}");
            }
        }

        if (profile.Audio is { } audio && !string.IsNullOrEmpty(audio.EndpointId))
        {
            if (!_audio.SetDefaultOutputDevice(audio.EndpointId))
            {
                success = false;
                messages.Add($"Audio device '{audio.FriendlyName}' could not be selected.");
            }
            else if (_audio.GetDefaultOutputDevice()?.Id == audio.EndpointId)
            {
                // Confirmed it actually became the active default — safe to play the tone on it.
                _audio.PlayConfirmation(audio.EndpointId);
            }
            else
            {
                // Set succeeded but the endpoint isn't currently available (e.g. TV/soundbar powered off).
                // Keep it selected (Windows routes to it once ready); don't play the tone on the old device.
                messages.Add($"Audio device '{audio.FriendlyName}' isn't available yet; it will be used once it's ready.");
            }
        }

        _log.Info($"Activation of '{profile.Name}' complete: success={success}. {string.Join("; ", messages)}");
        return new ActivationResult(success, messages);
    }
}
