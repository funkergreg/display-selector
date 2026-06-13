namespace DisplaySelector.UI;

/// <summary>A small modal that picks one item from a list (reused for device + profile selection).</summary>
internal sealed class ListPickerDialog<T> : Form
    where T : class
{
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly IReadOnlyList<T> _items;

    private ListPickerDialog(string title, string prompt, IReadOnlyList<T> items, Func<T, string> display, T? initial)
    {
        _items = items;

        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(380, 260);

        foreach (var item in items)
        {
            _list.Items.Add(display(item));
        }

        if (initial is not null)
        {
            var index = items.ToList().IndexOf(initial);
            if (index >= 0)
            {
                _list.SelectedIndex = index;
            }
        }
        else if (_list.Items.Count > 0)
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

    private T? Selected =>
        _list.SelectedIndex >= 0 && _list.SelectedIndex < _items.Count ? _items[_list.SelectedIndex] : null;

    public static T? Pick(string title, string prompt, IReadOnlyList<T> items, Func<T, string> display, T? initial = null)
    {
        using var dialog = new ListPickerDialog<T>(title, prompt, items, display, initial);
        return dialog.ShowDialog() == DialogResult.OK ? dialog.Selected : null;
    }
}
