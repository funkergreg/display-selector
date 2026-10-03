using DisplaySelector.Core.Audio;
using DisplaySelector.Core.Display;
using DisplaySelector.Core.Logging;
using DisplaySelector.Core.Profiles;

namespace DisplaySelector.Core.Activation;

/// <summary>
/// The single answer to "what's live right now". <see cref="ActiveProfileId"/> is the profile the
/// current displays + default audio device match (the tray check mark, the Profile Manager marker, the
/// tray tooltip), and <see cref="Snapshot"/> is the hardware state it was matched against, which the live
/// windows show. Both are cached and recomputed by <see cref="Refresh"/>, which the controller calls after
/// its own changes and when Windows reports a display or audio change (whoever made it), so no polling.
/// Skip decisions don't use this cache: <see cref="ProfileActivator.IsLive(Profile)"/> checks one profile
/// fresh and exactly at switch time.
/// </summary>
public sealed class LiveStateTracker
{
    private readonly IDisplayService _display;
    private readonly IAudioService _audio;
    private readonly ProfileActivator _activator;
    private readonly Func<IReadOnlyList<Profile>> _profiles;
    private readonly ILog _log;

    public LiveStateTracker(
        IDisplayService display,
        IAudioService audio,
        ProfileActivator activator,
        Func<IReadOnlyList<Profile>> profiles,
        ILog log)
    {
        _display = display;
        _audio = audio;
        _activator = activator;
        _profiles = profiles;
        _log = log;
    }

    /// <summary>What the live state is called when no Profile matches it (tray, Display Tester, diagnostics).</summary>
    public const string CustomName = "Custom (unsaved)";

    /// <summary>The profile matching the live hardware as of the last <see cref="Refresh"/>, or null (custom).</summary>
    public string? ActiveProfileId { get; private set; }

    /// <summary>The live hardware as of the last successful <see cref="Refresh"/>.</summary>
    public LiveSnapshot Snapshot { get; private set; } = LiveSnapshot.Empty;

    /// <summary>
    /// Re-reads the hardware into a new <see cref="Snapshot"/> and recomputes <see cref="ActiveProfileId"/>
    /// from it (coarse match; ties go to the exactly-live profile, so it agrees with "Already on"). Returns
    /// whether the active Profile changed. Never throws: a failed query keeps the previous snapshot and answer.
    /// </summary>
    public bool Refresh()
    {
        LiveSnapshot snapshot;
        string? active;
        try
        {
            snapshot = new LiveSnapshot(_display, _audio);
            active = ProfileMatching.FindActive(
                _profiles(),
                snapshot.ActiveDisplays,
                snapshot.DefaultAudioId,
                p => _activator.IsLive(p, snapshot.DefaultAudioId))?.Id;
        }
        catch (Exception ex)
        {
            _log.Debug($"Live-state refresh failed ({ex.Message}); keeping the previous active Profile.");
            return false;
        }

        Snapshot = snapshot;
        if (active == ActiveProfileId)
        {
            return false;
        }

        _log.Info($"Active Profile: {NameOf(ActiveProfileId)} → {NameOf(active)}.");
        ActiveProfileId = active;
        return true;
    }

    private string NameOf(string? id) =>
        id is null ? "custom" : _profiles().FirstOrDefault(p => p.Id == id)?.Name ?? "(deleted)";
}
