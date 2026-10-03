using DisplaySelector.Core.Audio;

namespace DisplaySelector.UI;

/// <summary>
/// The output-device list shared by the Audio Tester and the Audio Device Setter: one row per device
/// (<see cref="AudioEndpoint.DisplayLabel"/>), reloaded on Refresh.
/// </summary>
internal sealed class AudioDeviceList : ListBox
{
    private readonly IAudioService _audio;
    private List<AudioEndpoint> _endpoints = new();

    public AudioDeviceList(IAudioService audio)
    {
        _audio = audio;
        Dock = DockStyle.Fill;
        IntegralHeight = false;
    }

    public AudioEndpoint? Selected =>
        SelectedIndex >= 0 && SelectedIndex < _endpoints.Count ? _endpoints[SelectedIndex] : null;

    /// <summary>
    /// Re-reads the devices and selects <paramref name="selectId"/>; otherwise the Windows default, else
    /// the first.
    /// </summary>
    public void Reload(string? selectId = null)
    {
        _endpoints = _audio.GetOutputDevices().ToList();
        Items.Clear();
        foreach (var endpoint in _endpoints)
        {
            Items.Add(endpoint.DisplayLabel);
        }

        var index = _endpoints.FindIndex(e => e.Id == selectId);
        if (index < 0)
        {
            index = _endpoints.FindIndex(e => e.IsDefault);
        }

        SelectedIndex = index >= 0 ? index : _endpoints.Count > 0 ? 0 : -1;
    }
}
