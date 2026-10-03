using DisplaySelector.Core.Audio;
using DisplaySelector.Core.Logging;

namespace DisplaySelector.UI;

/// <summary>
/// Picks a Profile's audio device ("Set audio device…"). Refresh picks up a device turned on meanwhile,
/// and Play tone checks which device is which without changing the Windows default.
/// </summary>
internal sealed class AudioDeviceSetterDialog : Form
{
    private readonly IAudioService _audio;
    private readonly ILog _log;
    private readonly AudioDeviceList _list;
    private readonly Button _refreshButton = new() { Text = "Refresh", Width = 80 };
    private readonly Button _playButton = new() { Text = "Play tone", Width = 90 };
    private readonly Button _okButton = new() { Text = "OK", DialogResult = DialogResult.OK, Width = 80 };

    private AudioDeviceSetterDialog(IAudioService audio, ILog log, string prompt, string? currentEndpointId)
    {
        _audio = audio;
        _log = log;
        _list = new AudioDeviceList(audio);

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
        tools.Controls.AddRange(new Control[] { _refreshButton, _playButton });
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

        _refreshButton.Click += (_, _) => LoadDevices(Selected?.Id);
        _playButton.Click += (_, _) => PlaySelected();
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _list.DoubleClick += (_, _) =>
        {
            if (Selected is not null)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        };

        LoadDevices(currentEndpointId);
    }

    private AudioEndpoint? Selected => _list.Selected;

    /// <summary>The chosen device, or null if cancelled.</summary>
    public static AudioEndpoint? Pick(IAudioService audio, ILog log, string prompt, string? currentEndpointId)
    {
        using var dialog = new AudioDeviceSetterDialog(audio, log, prompt, currentEndpointId);
        return dialog.ShowDialog() == DialogResult.OK ? dialog.Selected : null;
    }

    // Keeps the selection by device id across a refresh; otherwise selects the Windows default.
    private void LoadDevices(string? selectId)
    {
        _list.Reload(selectId);
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        _okButton.Enabled = Selected is not null;
        _playButton.Enabled = Selected is not null;
    }

    // async void: a UI event handler; PlayConfirmationAsync never throws.
    private async void PlaySelected()
    {
        if (Selected is not { } endpoint)
        {
            return;
        }

        _playButton.Enabled = false;
        _log.Info($"Audio Device Setter: playing tone on '{endpoint.FriendlyName}'.");
        await _audio.PlayConfirmationAsync(endpoint.Id); // background thread; the window stays responsive
        if (!IsDisposed)
        {
            UpdateButtons();
        }
    }
}
