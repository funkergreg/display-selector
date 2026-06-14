namespace DisplaySelector.UI;

/// <summary>A small modal prompt for a single line of text (used for Save / Rename).</summary>
internal sealed class TextInputDialog : Form
{
    private readonly TextBox _input = new() { Dock = DockStyle.Fill };

    public TextInputDialog(string title, string prompt, string initialValue = "")
    {
        Text = title;
        Icon = AppIcon.Window;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(360, 120);

        var label = new Label { Text = prompt, Dock = DockStyle.Top, Height = 24 };
        _input.Text = initialValue;
        _input.SelectAll();

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 80 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 80 };
        AcceptButton = ok;
        CancelButton = cancel;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8),
        };
        buttons.Controls.AddRange(new Control[] { ok, cancel });

        var inputPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        inputPanel.Controls.Add(_input);

        Controls.Add(inputPanel);
        Controls.Add(label);
        Controls.Add(buttons);
    }

    public string Value => _input.Text.Trim();

    /// <summary>Shows the dialog and returns the trimmed, non-empty value, or null if cancelled/blank.</summary>
    public static string? Prompt(string title, string prompt, string initialValue = "")
    {
        using var dialog = new TextInputDialog(title, prompt, initialValue);
        return dialog.ShowDialog() == DialogResult.OK && dialog.Value.Length > 0 ? dialog.Value : null;
    }
}
