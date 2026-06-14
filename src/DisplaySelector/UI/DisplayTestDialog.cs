using System.Text;
using DisplaySelector.Core.Display;
using DisplaySelector.Core.Logging;

namespace DisplaySelector.UI;

/// <summary>
/// Human-in-the-loop (tier-3) display verification: shows the displays the tool currently sees
/// (so the human can confirm identification is correct), validates the apply path non-destructively,
/// and can re-apply the current configuration (the "unstick a frozen display UI" action).
/// </summary>
internal sealed class DisplayTestDialog : Form
{
    private readonly IDisplayService _display;
    private readonly ILog _log;
    private readonly TextBox _output = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Font = new Font(FontFamily.GenericMonospace, 9f),
    };

    private readonly Button _refreshButton = new() { Text = "Refresh", Width = 90 };
    private readonly Button _validateButton = new() { Text = "Validate", Width = 90 };
    private readonly Button _reapplyButton = new() { Text = "Re-apply current", Width = 130 };
    private readonly Button _closeButton = new() { Text = "Close", Width = 80 };

    public DisplayTestDialog(IDisplayService display, ILog log)
    {
        _display = display;
        _log = log;

        Text = "Display test";
        Icon = AppIcon.Window;
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(560, 360);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            Height = 44,
            Padding = new Padding(8),
        };
        buttons.Controls.AddRange(new Control[] { _refreshButton, _validateButton, _reapplyButton, _closeButton });

        var outputPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        outputPanel.Controls.Add(_output);

        Controls.Add(outputPanel);
        Controls.Add(buttons);

        _refreshButton.Click += (_, _) => Refresh_();
        _validateButton.Click += (_, _) => ValidateConfig();
        _reapplyButton.Click += (_, _) => Reapply();
        _closeButton.Click += (_, _) => Close();

        Refresh_();
    }

    private void Refresh_()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Displays the tool currently sees:");
        sb.AppendLine();

        var displays = _display.GetCurrentDisplays();
        if (displays.Count == 0)
        {
            sb.AppendLine("  (none — QueryDisplayConfig returned nothing)");
        }

        var n = 1;
        foreach (var d in displays)
        {
            sb.AppendLine($"  [{n++}] {d.Friendly}{(d.Primary ? "  (PRIMARY)" : string.Empty)}");
            sb.AppendLine($"        port key : {d.StableId}");
            sb.AppendLine($"        edid     : {d.Edid ?? "-"}");
            sb.AppendLine($"        resolution: {d.Resolution ?? "-"}   orientation: {d.Orientation ?? "-"}");
            sb.AppendLine();
        }

        _output.Text = sb.ToString();
    }

    private void ValidateConfig()
    {
        var ok = _display.ValidateCurrent();
        _log.Info($"Display test: ValidateCurrent => {ok}.");
        MessageBox.Show(
            this,
            ok ? "Current configuration validated successfully (settable)." : "Validation FAILED — see the log.",
            "Validate",
            MessageBoxButtons.OK,
            ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void Reapply()
    {
        var confirm = MessageBox.Show(
            this,
            "Re-apply the current display configuration now? Displays may briefly blank. " +
            "This is the same action that unsticks a frozen Windows display UI.",
            "Re-apply current",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.OK)
        {
            return;
        }

        var result = _display.ReapplyCurrent();
        _log.Info($"Display test: ReapplyCurrent => success={result.Success} error={result.Error ?? "-"}.");
        MessageBox.Show(
            this,
            result.Success ? "Re-applied current configuration." : $"Re-apply failed: {result.Error}",
            "Re-apply current",
            MessageBoxButtons.OK,
            result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        Refresh_();
    }
}
