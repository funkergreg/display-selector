using DisplaySelector.Core.Hotkeys;
using DisplaySelector.Core.Profiles;

namespace DisplaySelector.UI;

/// <summary>
/// Captures a key + modifiers (Ctrl/Alt/Shift) into a <see cref="HotkeyBinding"/>. OK is enabled only
/// once a non-modifier key is pressed; warns on risky bindings (no modifier + a typing key).
/// </summary>
internal sealed class HotkeyCaptureDialog : Form
{
    private readonly Label _capture = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font(FontFamily.GenericSansSerif, 12f, FontStyle.Bold),
        Text = "Press a key combination…",
    };

    private readonly Button _ok = new() { Text = "OK", Width = 80, Enabled = false };
    private readonly Button _clear = new() { Text = "Clear Hotkey", Width = 100 };
    private readonly Button _cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 80 };

    public HotkeyCaptureDialog(HotkeyBinding? current)
    {
        Text = "Set hotkey";
        Icon = AppIcon.Window;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(360, 150);
        KeyPreview = true;

        Binding = current;
        if (current is not null)
        {
            _capture.Text = HotkeyCodec.Format(current);
            _ok.Enabled = true;
        }

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8),
        };
        buttons.Controls.AddRange(new Control[] { _ok, _cancel, _clear });

        var capturePanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        capturePanel.Controls.Add(_capture);

        Controls.Add(capturePanel);
        Controls.Add(buttons);

        CancelButton = _cancel;

        _ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        _clear.Click += (_, _) =>
        {
            Binding = null;
            _capture.Text = "(none)";
            _ok.Enabled = true; // allow clearing the hotkey
        };

        KeyDown += OnKeyDown;
    }

    public HotkeyBinding? Binding { get; private set; }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;

        var keyCode = e.KeyCode;
        if (IsModifierKey(keyCode))
        {
            return; // wait for a non-modifier key
        }

        var modifiers = new List<string>();
        if (e.Control)
        {
            modifiers.Add("Control");
        }

        if (e.Alt)
        {
            modifiers.Add("Alt");
        }

        if (e.Shift)
        {
            modifiers.Add("Shift");
        }

        var binding = new HotkeyBinding { Key = keyCode.ToString() };
        binding.Modifiers.AddRange(modifiers);

        Binding = binding;
        _capture.Text = HotkeyCodec.Format(binding) + (HotkeyCodec.IsRisky(binding) ? "  ⚠ may hijack typing" : string.Empty);
        _ok.Enabled = true;
    }

    private static bool IsModifierKey(Keys key) =>
        key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu
            or Keys.LControlKey or Keys.RControlKey
            or Keys.LShiftKey or Keys.RShiftKey
            or Keys.LMenu or Keys.RMenu
            or Keys.LWin or Keys.RWin;
}
