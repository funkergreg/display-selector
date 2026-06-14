using DisplaySelector.Core.Audio;
using DisplaySelector.Core.Logging;

namespace DisplaySelector.UI;

/// <summary>
/// Human-in-the-loop (tier-3) audio verification: list endpoints, play the confirmation tone to a
/// chosen device and confirm it was heard, and optionally make a device the system default (all roles)
/// to verify the core switch — including that System Sounds follows.
/// </summary>
internal sealed class AudioTestDialog : Form
{
    private readonly IAudioService _audio;
    private readonly ILog _log;
    private readonly Action<AudioEndpoint>? _onAssignToProfile;
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Button _playButton = new() { Text = "Play tone", Width = 100 };
    private readonly Button _setDefaultButton = new() { Text = "Set as default", Width = 110 };
    private readonly Button _assignButton = new() { Text = "Assign to profile…", Width = 130 };
    private readonly Button _refreshButton = new() { Text = "Refresh", Width = 80 };
    private readonly Button _closeButton = new() { Text = "Close", Width = 70 };

    private List<AudioEndpoint> _endpoints = new();

    public AudioTestDialog(IAudioService audio, ILog log, Action<AudioEndpoint>? onAssignToProfile = null)
    {
        _audio = audio;
        _log = log;
        _onAssignToProfile = onAssignToProfile;

        Text = "Audio test";
        Icon = AppIcon.Window;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(520, 300);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            Height = 44,
            Padding = new Padding(8),
        };
        buttons.Controls.Add(_playButton);
        buttons.Controls.Add(_setDefaultButton);
        if (_onAssignToProfile is not null)
        {
            buttons.Controls.Add(_assignButton);
        }

        buttons.Controls.Add(_refreshButton);
        buttons.Controls.Add(_closeButton);

        var listPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        listPanel.Controls.Add(_list);

        Controls.Add(listPanel);
        Controls.Add(buttons);

        _playButton.Click += (_, _) => PlaySelected();
        _setDefaultButton.Click += (_, _) => SetSelectedAsDefault();
        _assignButton.Click += (_, _) => AssignSelectedToProfile();
        _refreshButton.Click += (_, _) => LoadDevices();
        _closeButton.Click += (_, _) => Close();

        LoadDevices();
    }

    private void LoadDevices()
    {
        _endpoints = _audio.GetOutputDevices().ToList();
        _list.Items.Clear();
        foreach (var e in _endpoints)
        {
            _list.Items.Add(e.IsDefault ? $"{e.FriendlyName}  (default)" : e.FriendlyName);
        }

        if (_list.Items.Count > 0)
        {
            _list.SelectedIndex = Math.Max(0, _endpoints.FindIndex(e => e.IsDefault));
        }

        var any = _endpoints.Count > 0;
        _playButton.Enabled = any;
        _setDefaultButton.Enabled = any;
        _assignButton.Enabled = any;
    }

    private void AssignSelectedToProfile()
    {
        if (Selected is { } endpoint)
        {
            _onAssignToProfile?.Invoke(endpoint);
        }
    }

    private AudioEndpoint? Selected =>
        _list.SelectedIndex >= 0 && _list.SelectedIndex < _endpoints.Count ? _endpoints[_list.SelectedIndex] : null;

    private void PlaySelected()
    {
        if (Selected is not { } endpoint)
        {
            return;
        }

        _playButton.Enabled = false;
        _log.Info($"Audio test: playing tone on '{endpoint.FriendlyName}'.");

        // Play off the UI thread so the window stays responsive, then ask the human to confirm.
        Task.Run(() => _audio.PlayConfirmation(endpoint.Id))
            .ContinueWith(
                _ => PromptHeard(endpoint),
                TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void PromptHeard(AudioEndpoint endpoint)
    {
        _playButton.Enabled = true;
        var heard = MessageBox.Show(
            this,
            $"Did you hear the tone on '{endpoint.FriendlyName}'?",
            "Audio test",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        _log.Info($"Audio test result for '{endpoint.FriendlyName}': {(heard == DialogResult.Yes ? "HEARD" : "NOT heard")}.");
    }

    private void SetSelectedAsDefault()
    {
        if (Selected is not { } endpoint)
        {
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"Make '{endpoint.FriendlyName}' the default output for all roles (apps + System Sounds)?",
            "Set default device",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.OK)
        {
            return;
        }

        var ok = _audio.SetDefaultOutputDevice(endpoint.Id);
        if (ok)
        {
            _audio.PlayConfirmation(endpoint.Id);
        }
        else
        {
            MessageBox.Show(this, "Failed to set the default device. See the log for details.", "Set default device", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        LoadDevices();
    }
}
