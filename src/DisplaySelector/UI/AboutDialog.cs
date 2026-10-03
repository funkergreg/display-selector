using DisplaySelector.Core;

namespace DisplaySelector.UI;

/// <summary>
/// The About window: app name, description, version, project links, and author. A plain window (not a
/// toast) so it works for users who have Windows notifications turned off. Closed with the title-bar
/// ✕ or Esc — no separate Close button.
/// </summary>
internal sealed class AboutDialog : Form
{
    // Every row shares this margin so the text lines up flush on the left.
    private static readonly Padding RowMargin = new(0, 2, 0, 2);

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
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
        };

        var title = new Label
        {
            Text = AppIdentity.AppName,
            AutoSize = true,
            Font = new Font(Font.FontFamily, Font.Size * 1.5f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 4),
        };
        var description = new Label
        {
            Text = "Switch display + audio profiles with a hotkey.",
            AutoSize = true,
            Margin = RowMargin,
        };
        var versionLabel = new Label { Text = $"Version: {version}", AutoSize = true, Margin = new Padding(0, 2, 0, 12) };

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Padding = new Padding(16),
            MinimumSize = new Size(320, 0),
        };
        layout.Controls.Add(title);
        layout.Controls.Add(description);
        layout.Controls.Add(versionLabel);
        layout.Controls.Add(CreateLink("View on GitHub: ", AppIdentity.ProjectUrl, AppIdentity.ProjectUrl, openLink));
        layout.Controls.Add(CreateLink("Website: ", AppIdentity.ProjectSiteUrl, AppIdentity.ProjectSiteUrl, openLink));
        layout.Controls.Add(CreateLink($"Author: {AppIdentity.AuthorName}   ", "Buy Me A Coffee", AppIdentity.DonateUrl, openLink));

        Controls.Add(layout);
    }

    // "<prefix><link text>", where only the link text is clickable and opens the url.
    private static LinkLabel CreateLink(string prefix, string linkText, string url, Action<string> openLink)
    {
        var link = new LinkLabel { Text = prefix + linkText, AutoSize = true, Margin = RowMargin };
        link.LinkArea = new LinkArea(prefix.Length, linkText.Length);
        link.LinkClicked += (_, _) => openLink(url);
        return link;
    }
}
