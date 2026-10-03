namespace DisplaySelector.UI;

/// <summary>
/// Profile Manager ▸ Show diagnostics: one Profile's saved displays, audio device, hotkey and desktop
/// items next to how they are right now (<c>ProfileReport</c>). Modeless and single-instance; it follows
/// the Profile Manager's selection (<see cref="ShowProfile"/>) and the controller refreshes it on live
/// changes (<see cref="RefreshReport"/>).
/// </summary>
internal sealed class ProfileDiagnosticsForm : Form
{
    private readonly Func<string, string?> _buildReport;
    // RichTextBox: scrollbars only when the text doesn't fit (a TextBox always shows them).
    private readonly RichTextBox _text = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        WordWrap = false,
        DetectUrls = false,
        ScrollBars = RichTextBoxScrollBars.Both,
        BorderStyle = BorderStyle.None,
        BackColor = SystemColors.Window,
    };
    private readonly Button _copyButton = new() { Text = "Copy", AutoSize = true };
    private readonly FlowLayoutPanel _buttons = new()
    {
        Dock = DockStyle.Bottom,
        AutoSize = true,
        FlowDirection = FlowDirection.LeftToRight,
        Padding = new Padding(8),
    };
    private readonly Panel _body = new() { Dock = DockStyle.Fill, Padding = new Padding(8) };
    private string? _profileId;
    private string _report = string.Empty;
    private bool _fitted;

    /// <param name="profileId">The Profile to show first.</param>
    /// <param name="buildReport">Profile id → report text, or null when the Profile no longer exists.</param>
    /// <param name="copy">Puts the report on the clipboard (and says so).</param>
    public ProfileDiagnosticsForm(string profileId, Func<string, string?> buildReport, Action<string> copy)
    {
        _buildReport = buildReport;

        Text = "Profile Diagnostics";
        Icon = AppIcon.Window;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(560, 360);
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
        };

        _buttons.Controls.Add(_copyButton);
        _body.Controls.Add(_text);
        Controls.Add(_body);
        Controls.Add(_buttons);

        _copyButton.Click += (_, _) => copy(_report); // the built text: the box would hand back LF-only line ends
        ShowProfile(profileId); // before the first fit, so the window opens at its final size
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ActiveControl = _copyButton; // so the text isn't shown selected
        FitToContent();
        CenterToScreen();
    }

    /// <summary>Shows another Profile (the Profile Manager's selection changed).</summary>
    public void ShowProfile(string id)
    {
        if (id == _profileId)
        {
            return;
        }

        _profileId = id;
        RefreshReport();
    }

    /// <summary>
    /// Re-reads the current state for the shown Profile. An unchanged report leaves the text alone, so a
    /// live refresh (any device plugged in) doesn't reset the scroll position or the user's selection.
    /// </summary>
    public void RefreshReport()
    {
        if (IsDisposed || _profileId is null)
        {
            return;
        }

        var report = _buildReport(_profileId) ?? "This Profile no longer exists.";
        if (report == _report)
        {
            return;
        }

        _report = report;
        _text.Text = report;
        _text.Select(0, 0);
        FitToContent();
    }

    // Big enough for the report without scrolling (within the screen); grows, never shrinks, afterwards.
    private void FitToContent()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        var textSize = TextRenderer.MeasureText(_text.Text, _text.Font);
        var scrollbar = SystemInformation.GetVerticalScrollBarWidthForDpi(DeviceDpi);
        var client = new Size(
            Math.Max(textSize.Width + scrollbar + _body.Padding.Horizontal + LogicalToDeviceUnits(8), LogicalToDeviceUnits(420)),
            textSize.Height + scrollbar + _body.Padding.Vertical + _buttons.GetPreferredSize(Size.Empty).Height + LogicalToDeviceUnits(8));
        WindowFit.Fit(this, client, growOnly: _fitted);
        _fitted = true;
    }
}
