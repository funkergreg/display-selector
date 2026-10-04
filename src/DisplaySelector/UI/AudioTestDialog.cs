using DisplaySelector.Core.Audio;
using DisplaySelector.Core.Logging;

namespace DisplaySelector.UI;

/// <summary>
/// Human-in-the-loop (tier-3) audio verification: list endpoints, play the confirmation tone to a
/// chosen device and confirm it was heard, and optionally make a device the system default (all roles)
/// to verify the core switch — including that System Sounds follows. Can also assign the device to one
/// Profile, or to every full (non-audio-only) Profile.
/// </summary>
internal sealed class AudioTestDialog : Form
{
    private readonly IAudioService _audio;
    private readonly ILog _log;
    private readonly AudioDeviceList _list;
    private readonly Button _playButton = new() { Text = "Play tone", AutoSize = true };
    private readonly Button _setDefaultButton = new() { Text = "Set as Windows default", AutoSize = true };
    private readonly Button _assignButton = new() { Text = "Assign to Profile…", AutoSize = true };
    private readonly Button _assignAllButton = new() { Text = "Assign to all Profiles…", AutoSize = true };
    private readonly Button _refreshButton = new() { Text = "Refresh", AutoSize = true };
    private readonly FlowLayoutPanel _buttons = new()
    {
        Dock = DockStyle.Bottom,
        AutoSize = true,
        WrapContents = false,
        FlowDirection = FlowDirection.LeftToRight,
        Padding = new Padding(8),
    };

    public AudioTestDialog(
        IAudioService audio,
        ILog log,
        Action<AudioEndpoint> onAssignToProfile,
        Action<AudioEndpoint> onAssignToAllProfiles)
    {
        _audio = audio;
        _log = log;
        _list = new AudioDeviceList(audio);

        Text = "Audio Tester";
        Icon = AppIcon.Window;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(520, 300);

        // Refresh first (leftmost) to match the Display Tester. The window's [x] closes it, so
        // there's no redundant Close button.
        _buttons.Controls.Add(_refreshButton);
        _buttons.Controls.Add(_playButton);
        _buttons.Controls.Add(_setDefaultButton);
        _buttons.Controls.Add(_assignButton);
        _buttons.Controls.Add(_assignAllButton);

        var listPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        listPanel.Controls.Add(_list);

        Controls.Add(listPanel);
        Controls.Add(_buttons);

        _playButton.Click += (_, _) => _list.PlaySelected(_playButton, _log, "Audio Tester");
        _setDefaultButton.Click += (_, _) => SetSelectedAsDefault();
        _assignButton.Click += (_, _) => RunOnSelected(onAssignToProfile);
        _assignAllButton.Click += (_, _) => RunOnSelected(onAssignToAllProfiles);
        _refreshButton.Click += (_, _) => LoadDevices();

        LoadDevices();
    }

    // Selects the Windows default.
    private void LoadDevices()
    {
        _list.Reload();
        UpdateButtons();
    }

    /// <summary>
    /// Shows a live refresh's device list (the default moved, or a device came or went), so the
    /// "(default)" marker stays current. Keeps the selected device; unchanged devices leave it alone.
    /// </summary>
    public void RefreshDevices(IReadOnlyList<AudioEndpoint> devices)
    {
        if (!IsDisposed && _list.ShowIfChanged(devices))
        {
            UpdateButtons();
        }
    }

    private void UpdateButtons()
    {
        var any = _list.Items.Count > 0;
        _playButton.Enabled = any && !_list.IsPlaying;
        _setDefaultButton.Enabled = any;
        _assignButton.Enabled = any;
        _assignAllButton.Enabled = any;
    }

    // The buttons may not fit the default width (longer labels at higher scaling): widen to fit them.
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        WindowFit.Fit(this, new Size(_buttons.GetPreferredSize(Size.Empty).Width, ClientSize.Height), growOnly: true);
        CenterToScreen();
    }

    private void RunOnSelected(Action<AudioEndpoint> action)
    {
        if (_list.Selected is { } endpoint)
        {
            action(endpoint);
        }
    }

    private void SetSelectedAsDefault()
    {
        if (_list.Selected is not { } endpoint)
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
            _ = _audio.PlayConfirmationAsync(endpoint.Id); // background; never blocks the window
        }
        else
        {
            MessageBox.Show(this, "Failed to set the default device. See the log for details.", "Set default device", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        LoadDevices();
    }
}
