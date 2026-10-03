using DisplaySelector.Core.Launch;

namespace DisplaySelector.UI;

/// <summary>
/// The short confirm step of "Create a Launcher…": shows which Profile and target the Launcher ties
/// together, and lets the user adjust its name and add optional arguments before it lands on the desktop.
/// </summary>
internal sealed class LauncherDetailsDialog : Form
{
    private readonly string _defaultName;
    private readonly TextBox _name = new() { Dock = DockStyle.Fill };
    private readonly TextBox _arguments = new() { Dock = DockStyle.Fill, PlaceholderText = "Optional: passed to the game, e.g. -fullscreen" };

    public LauncherDetailsDialog(string profileName, string target)
    {
        _defaultName = ShortcutNaming.DefaultName(profileName, target);

        Text = "Create a Launcher";
        Icon = AppIcon.Window;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(520, 200);

        var intro = new Label
        {
            Text = "Double-clicking the Launcher on your desktop switches to the Profile, waits for the displays " +
                   "to settle, then starts the game.",
            Dock = DockStyle.Fill,
            AutoSize = false,
        };

        _name.Text = _defaultName;

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(8),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        for (var i = 0; i < 4; i++)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        grid.Controls.Add(intro, 0, 0);
        grid.SetColumnSpan(intro, 2);
        grid.Controls.Add(FieldLabel("Profile:"), 0, 1);
        grid.Controls.Add(ValueLabel(profileName), 1, 1);
        grid.Controls.Add(FieldLabel("Launches:"), 0, 2);
        grid.Controls.Add(ValueLabel(target), 1, 2);
        grid.Controls.Add(FieldLabel("Launcher name:"), 0, 3);
        grid.Controls.Add(_name, 1, 3);
        grid.Controls.Add(FieldLabel("Arguments:"), 0, 4);
        grid.Controls.Add(_arguments, 1, 4);

        var ok = new Button { Text = "Create", DialogResult = DialogResult.OK, Width = 80 };
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

        Controls.Add(grid);
        Controls.Add(buttons);
    }

    /// <summary>A file-name-safe launcher name (falls back to the default when left blank).</summary>
    public string LauncherName
    {
        get
        {
            var name = ShortcutNaming.Sanitize(_name.Text);
            return name.Length > 0 ? name : _defaultName;
        }
    }

    public string? Arguments
    {
        get
        {
            var text = _arguments.Text.Trim();
            return text.Length > 0 ? text : null;
        }
    }

    private static Label FieldLabel(string text) =>
        new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) };

    // Long paths are ellipsized rather than widening the dialog.
    private static Label ValueLabel(string text) =>
        new()
        {
            Text = text,
            AutoSize = false,
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Height = 23,
        };
}
