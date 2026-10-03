namespace DisplaySelector.Core.Audio;

/// <summary>
/// Audio capture/apply, behind an interface so the undocumented COM (see <c>Interop/PolicyConfig.cs</c>)
/// stays mockable and swappable (CLAUDE.md "here be dragons" #2).
/// </summary>
public interface IAudioService
{
    /// <summary>All active render (output) endpoints, with the current default flagged.</summary>
    IReadOnlyList<AudioEndpoint> GetOutputDevices();

    /// <summary>The current default render endpoint (multimedia role), or null if none.</summary>
    AudioEndpoint? GetDefaultOutputDevice();

    /// <summary>Whether the endpoint exists and is active (plugged in / powered on). Never throws.</summary>
    bool IsDeviceActive(string endpointId);

    /// <summary>
    /// Set the default output endpoint for ALL roles (Console, Multimedia, Communications) so every
    /// app and System Sounds follows. Returns false (and logs) on failure; never throws.
    /// </summary>
    bool SetDefaultOutputDevice(string endpointId);

    /// <summary>
    /// Render a short confirmation tone to the given endpoint (or the default when null) on a background
    /// thread. Completes when it's done, or after a few seconds at most: a stalled endpoint is
    /// abandoned, never waited on forever. Never throws.
    /// </summary>
    Task PlayConfirmationAsync(string? endpointId = null);

    /// <summary>
    /// Raised when the default output device may have changed (a new default, or an output device
    /// arriving / leaving), by this app or anyone else. Raised on a Windows audio worker thread, not the
    /// UI thread: handlers must marshal and must not block. Windows starts reporting on the first
    /// subscription.
    /// </summary>
    event Action? DefaultDeviceChanged;
}
