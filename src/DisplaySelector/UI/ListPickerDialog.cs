using DisplaySelector.App;
using DisplaySelector.Core.Profiles;

namespace DisplaySelector.UI;

/// <summary>
/// A small modal that picks one Profile from a list (the Profile pickers). Rows are owner-drawn like the
/// Profile Manager's: the Profile's glyph, then its shared label.
/// </summary>
internal sealed class ListPickerDialog : Form
{
    private readonly ListBox _list = new()
    {
        Dock = DockStyle.Fill,
        IntegralHeight = false,
        DrawMode = DrawMode.OwnerDrawFixed,
    };
    private readonly IReadOnlyList<Profile> _profiles;

    private ListPickerDialog(string title, string prompt, IReadOnlyList<Profile> profiles)
    {
        _profiles = profiles;

        Text = title;
        Icon = AppIcon.Window;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(380, 260);

        _list.DrawItem += DrawRow;
        foreach (var profile in profiles)
        {
            _list.Items.Add(ProfileLabels.Label(profile));
        }

        if (_list.Items.Count > 0)
        {
            _list.SelectedIndex = 0;
        }

        var label = new Label { Text = prompt, Dock = DockStyle.Top, Height = 24 };

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 80, Enabled = _list.SelectedIndex >= 0 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 80 };
        AcceptButton = ok;
        CancelButton = cancel;

        _list.SelectedIndexChanged += (_, _) => ok.Enabled = _list.SelectedIndex >= 0;
        _list.DoubleClick += (_, _) =>
        {
            if (_list.SelectedIndex >= 0)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8),
        };
        buttons.Controls.AddRange(new Control[] { ok, cancel });

        var listPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        listPanel.Controls.Add(_list);

        Controls.Add(listPanel);
        Controls.Add(label);
        Controls.Add(buttons);
    }

    private Profile? Selected =>
        _list.SelectedIndex >= 0 && _list.SelectedIndex < _profiles.Count ? _profiles[_list.SelectedIndex] : null;

    public static Profile? Pick(string title, string prompt, IReadOnlyList<Profile> profiles)
    {
        using var dialog = new ListPickerDialog(title, prompt, profiles);
        return dialog.ShowDialog() == DialogResult.OK ? dialog.Selected : null;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _list.ItemHeight = ProfileGlyphs.RowHeight(_list);
    }

    private void DrawRow(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _profiles.Count)
        {
            return;
        }

        e.DrawBackground();
        ProfileGlyphs.DrawRow(_list, e, _profiles[e.Index], ProfileLabels.Label(_profiles[e.Index]), _list.LogicalToDeviceUnits(4));
        e.DrawFocusRectangle();
    }
}
