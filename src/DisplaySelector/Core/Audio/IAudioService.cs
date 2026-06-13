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

    /// <summary>
    /// Set the default output endpoint for ALL roles (Console, Multimedia, Communications) so every
    /// app and System Sounds follows. Returns false (and logs) on failure; never throws.
    /// </summary>
    bool SetDefaultOutputDevice(string endpointId);

    /// <summary>Render a short confirmation tone to the given endpoint (or the default when null). Blocks until done.</summary>
    void PlayConfirmation(string? endpointId = null);
}
