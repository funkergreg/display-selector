using DisplaySelector.Core;

namespace DisplaySelector.UI;

/// <summary>
/// The About window: app name, version, and links to the project. A plain window (not a toast) so it
/// works for users who have Windows notifications turned off.
/// </summary>
internal sealed class AboutDialog : Form
{
    public AboutDialog(string version, Action<string> openLink)
    {
        Text = $"About {AppIdentity.AppName}";
        Icon = AppIcon.Window;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var title = new Label
        {
            Text = AppIdentity.AppName,
            AutoSize = true,
            Font = new Font(Font.FontFamily, Font.Size * 1.5f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 4),
        };
        var versionLabel = new Label { Text = $"Version {version}", AutoSize = true };
        var description = new Label
        {
            Text = "Switch display + audio profiles with a hotkey.",
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 12),
        };

        var close = new Button { Text = "Close", DialogResult = DialogResult.OK, Width = 80, Anchor = AnchorStyles.Right };
        AcceptButton = close;
        CancelButton = close;

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Padding = new Padding(16),
            MinimumSize = new Size(320, 0),
        };
        layout.Controls.Add(title);
        layout.Controls.Add(versionLabel);
        layout.Controls.Add(description);
        layout.Controls.Add(CreateLink("View on GitHub", AppIdentity.ProjectUrl, openLink));
        layout.Controls.Add(CreateLink("Website", AppIdentity.ProjectSiteUrl, openLink));
        close.Margin = new Padding(0, 16, 0, 0);
        layout.Controls.Add(close);

        Controls.Add(layout);
    }

    private static LinkLabel CreateLink(string label, string url, Action<string> openLink)
    {
        var link = new LinkLabel { Text = $"{label}: {url}", AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
        link.LinkArea = new LinkArea(label.Length + 2, url.Length);
        link.LinkClicked += (_, _) => openLink(url);
        return link;
    }
}
