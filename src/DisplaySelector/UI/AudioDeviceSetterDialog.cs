using DisplaySelector.Core.Audio;
using DisplaySelector.Core.Logging;

namespace DisplaySelector.UI;

/// <summary>
/// Picks a Profile's audio device ("Set audio device…"). Refresh picks up a device turned on meanwhile,
/// and Play tone checks which device is which without changing the Windows default.
/// </summary>
internal sealed class AudioDeviceSetterDialog : Form
{
    private readonly AudioDeviceList _list;
    private readonly Button _playButton = new() { Text = "Play tone", Width = 90 };
    private readonly Button _okButton = new() { Text = "OK", DialogResult = DialogResult.OK, Width = 80 };

    private AudioDeviceSetterDialog(IAudioService audio, ILog log, string prompt, string? currentEndpointId)
    {
        _list = new AudioDeviceList(audio);
        var refresh = new Button { Text = "Refresh", Width = 80 };

        Text = "Audio Device Setter";
        Icon = AppIcon.Window;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(460, 280);

        var label = new Label { Text = prompt, Dock = DockStyle.Top, Height = 24, Padding = new Padding(8, 6, 8, 0) };

        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 80 };
        AcceptButton = _okButton;
        CancelButton = cancel;

        // Device tools on the left, the dialog's answer on the right. No wrapping: a docked AutoSize flow
        // panel would otherwise stack its buttons one per line and clip the second.
        var tools = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
        tools.Controls.AddRange(new Control[] { refresh, _playButton });
        var answer = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.RightToLeft };
        answer.Controls.AddRange(new Control[] { cancel, _okButton });
        var buttons = new Panel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(8) };
        buttons.Controls.Add(tools);
        buttons.Controls.Add(answer);

        var listPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        listPanel.Controls.Add(_list);

        Controls.Add(listPanel);
        Controls.Add(label);
        Controls.Add(buttons);

        refresh.Click += (_, _) => LoadDevices(_list.Selected?.Id);
        _playButton.Click += (_, _) => _list.PlaySelected(_playButton, log, "Audio Device Setter");
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _list.DoubleClick += (_, _) =>
        {
            if (_list.Selected is not null)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        };

        LoadDevices(currentEndpointId);
    }

    /// <summary>The chosen device, or null if cancelled.</summary>
    public static AudioEndpoint? Pick(IAudioService audio, ILog log, string prompt, string? currentEndpointId)
    {
        using var dialog = new AudioDeviceSetterDialog(audio, log, prompt, currentEndpointId);
        return dialog.ShowDialog() == DialogResult.OK ? dialog._list.Selected : null;
    }

    // Keeps the selection by device id across a refresh; otherwise selects the Windows default.
    private void LoadDevices(string? selectId)
    {
        _list.Reload(selectId);
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        _okButton.Enabled = _list.Selected is not null;
        _playButton.Enabled = _list.Selected is not null && !_list.IsPlaying;
    }
}
