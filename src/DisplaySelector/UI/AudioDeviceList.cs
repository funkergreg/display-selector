using DisplaySelector.Core.Audio;
using DisplaySelector.Core.Logging;

namespace DisplaySelector.UI;

/// <summary>
/// The output-device list shared by the Audio Tester and the Audio Device Setter: one row per device
/// (<see cref="AudioEndpoint.DisplayLabel"/>), reloaded on Refresh, plus the shared Play tone action.
/// </summary>
internal sealed class AudioDeviceList : ListBox
{
    private readonly IAudioService _audio;

    public AudioDeviceList(IAudioService audio)
    {
        _audio = audio;
        Dock = DockStyle.Fill;
        IntegralHeight = false;
        DisplayMember = nameof(AudioEndpoint.DisplayLabel);
    }

    public AudioEndpoint? Selected => SelectedItem as AudioEndpoint;

    /// <summary>A tone is playing (Play tone stays disabled until it ends, whatever gets selected).</summary>
    public bool IsPlaying { get; private set; }

    /// <summary>
    /// Re-reads the devices (or shows <paramref name="devices"/>, already read) and selects
    /// <paramref name="selectId"/>; otherwise the Windows default, else the first.
    /// </summary>
    public void Reload(string? selectId = null, IReadOnlyList<AudioEndpoint>? devices = null)
    {
        var endpoints = (devices ?? _audio.GetOutputDevices()).ToList();
        Items.Clear();
        Items.AddRange(endpoints.ToArray<object>());

        var index = endpoints.FindIndex(e => e.Id == selectId);
        if (index < 0)
        {
            index = endpoints.FindIndex(e => e.IsDefault);
        }

        SelectedIndex = index >= 0 ? index : endpoints.Count > 0 ? 0 : -1;
    }

    /// <summary>
    /// Shows a live refresh's device list, keeping the selected device. Unchanged devices (a refresh fires
    /// for any device plugged in) leave the list alone. Returns whether it changed.
    /// </summary>
    public bool ShowIfChanged(IReadOnlyList<AudioEndpoint> devices)
    {
        if (Items.Cast<AudioEndpoint>().SequenceEqual(devices))
        {
            return false;
        }

        Reload(Selected?.Id, devices);
        return true;
    }

    /// <summary>
    /// Plays the confirmation tone on the selected device without changing the Windows default.
    /// <paramref name="button"/> is disabled while it plays (on a background thread, so the window stays
    /// responsive). async void: a UI event handler; PlayConfirmationAsync never throws.
    /// </summary>
    public async void PlaySelected(Button button, ILog log, string source)
    {
        if (IsPlaying || Selected is not { } endpoint)
        {
            return;
        }

        IsPlaying = true;
        button.Enabled = false;
        log.Info($"{source}: playing tone on '{endpoint.FriendlyName}'.");
        await _audio.PlayConfirmationAsync(endpoint.Id);
        IsPlaying = false;
        if (!button.IsDisposed)
        {
            button.Enabled = Selected is not null;
        }
    }
}
