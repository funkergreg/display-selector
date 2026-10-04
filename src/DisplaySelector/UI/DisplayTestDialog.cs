using DisplaySelector.Core.Activation;
using DisplaySelector.Core.Display;
using DisplaySelector.Core.Profiles;

namespace DisplaySelector.UI;

/// <summary>
/// Human-in-the-loop (tier-3) display check: every display Windows reports as connected, one row each,
/// with its port, layout, whether it's showing desktop, and whether the active Profile uses it, so the
/// human can confirm identification is right. Refreshes itself when displays change or devices are
/// plugged in or out (<see cref="RefreshDisplays"/>, called by the controller); Refresh stays as a manual
/// fallback. Hover a row for its EDID id (the fallback matching key).
/// </summary>
internal sealed class DisplayTestDialog : Form
{
    private static readonly string[] Columns = { "Display", "Port", "Resolution", "Orientation", "Primary", "Status", "In active Profile" };

    private readonly Func<LiveSnapshot> _live;
    private readonly Func<Profile?> _activeProfile;
    private readonly ListView _table = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HeaderStyle = ColumnHeaderStyle.Nonclickable,
        ShowItemToolTips = true,
    };
    private readonly Label _activeLabel = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 6) };
    private readonly Label _note = new()
    {
        Dock = DockStyle.Bottom,
        AutoSize = true,
        ForeColor = SystemColors.GrayText,
        Padding = new Padding(0, 6, 0, 0),
        Text = "Windows can tell whether a display is connected, not whether it's switched on: a TV that's off " +
               "may disappear from this list or stay listed as connected. Hover a row for its EDID id.",
    };
    private readonly Button _refreshButton = new() { Text = "Refresh", AutoSize = true };
    private readonly FlowLayoutPanel _buttons = new()
    {
        Dock = DockStyle.Bottom,
        AutoSize = true,
        FlowDirection = FlowDirection.LeftToRight,
        Padding = new Padding(8),
    };
    private readonly Panel _body = new() { Dock = DockStyle.Fill, Padding = new Padding(8, 8, 8, 0) };
    private string _shown = string.Empty;
    private bool _fitted;

    /// <param name="live">The hardware as of the last live-state refresh (shared with the check mark).</param>
    /// <param name="activeProfile">The active Profile as of the last live-state refresh.</param>
    /// <param name="refreshLiveState">
    /// Re-reads the live state now (the manual Refresh); the controller then refreshes this window.
    /// </param>
    public DisplayTestDialog(Func<LiveSnapshot> live, Func<Profile?> activeProfile, Action refreshLiveState)
    {
        _live = live;
        _activeProfile = activeProfile;

        Text = "Display Tester";
        Icon = AppIcon.Window;
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(640, 260);

        foreach (var column in Columns)
        {
            _table.Columns.Add(column);
        }

        // The window's [x] closes it, so there's no redundant Close button.
        _buttons.Controls.Add(_refreshButton);
        _body.Controls.Add(_table);
        _body.Controls.Add(_activeLabel);
        _body.Controls.Add(_note);

        Controls.Add(_body);
        Controls.Add(_buttons);

        _refreshButton.Click += (_, _) => refreshLiveState();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ActiveControl = _refreshButton; // nothing pre-selected
        RefreshDisplays();
        CenterToScreen();
    }

    /// <summary>
    /// Shows the latest live snapshot; the window grows (never shrinks) if the new rows don't fit. Unchanged
    /// contents are left alone (a live refresh fires for any device plugged in), and a changed table
    /// keeps the selected display selected.
    /// </summary>
    public void RefreshDisplays()
    {
        if (IsDisposed)
        {
            return;
        }

        var profile = _activeProfile();
        var label = $"Active Profile: {profile?.Name ?? LiveStateTracker.CustomName}";
        var live = _live();
        var rows = DisplayInventory.Rows(live.ActiveDisplays, live.ConnectedDisplays)
            .Select(row => (row.Target, Cells: new[]
            {
                row.Target.Friendly,
                row.Target.StableId,
                row.Target.Resolution ?? string.Empty,
                row.Target.Orientation ?? string.Empty,
                row.Target.Primary ? "Yes" : string.Empty,
                DisplayInventory.Describe(row.State),
                profile is null ? "–"
                    : profile.Display?.Targets.Any(t => t.StableId == row.Target.StableId) == true ? "Yes" : "No",
            }))
            .ToList();

        var shown = label + "\n" + string.Join("\n", rows.Select(r => string.Join("|", r.Cells) + "|" + r.Target.Edid));
        if (shown == _shown)
        {
            return;
        }

        _shown = shown;
        _activeLabel.Text = label;
        var selected = _table.SelectedItems.Count > 0 ? _table.SelectedItems[0].Name : null;

        _table.BeginUpdate();
        _table.Items.Clear();
        foreach (var (target, cells) in rows)
        {
            _table.Items.Add(new ListViewItem(cells)
            {
                Name = target.StableId,
                ToolTipText = $"EDID id: {target.Edid ?? "(none)"}",
                Selected = target.StableId == selected,
            });
        }

        if (rows.Count == 0)
        {
            _table.Items.Add(new ListViewItem("(no displays reported)") { ForeColor = SystemColors.GrayText });
        }

        SizeColumns();
        _table.EndUpdate();
        FitToContent();
    }

    // Each column as wide as its header or its widest cell.
    private void SizeColumns()
    {
        var padding = LogicalToDeviceUnits(16);
        for (var i = 0; i < _table.Columns.Count; i++)
        {
            var widest = TextRenderer.MeasureText(_table.Columns[i].Text, _table.Font).Width;
            foreach (ListViewItem item in _table.Items)
            {
                if (i < item.SubItems.Count)
                {
                    widest = Math.Max(widest, TextRenderer.MeasureText(item.SubItems[i].Text, _table.Font).Width);
                }
            }

            _table.Columns[i].Width = widest + padding;
        }
    }

    private void FitToContent()
    {
        if (!IsHandleCreated || _table.Items.Count == 0)
        {
            return;
        }

        var rowHeight = _table.GetItemRect(0).Height;
        var headerHeight = _table.GetItemRect(0).Top; // the first row starts below the header
        var tableWidth = _table.Columns.Cast<ColumnHeader>().Sum(c => c.Width) + LogicalToDeviceUnits(8);
        var tableHeight = headerHeight + (rowHeight * _table.Items.Count) + LogicalToDeviceUnits(8);

        // The note wraps to the table's width rather than widening the window.
        var width = Math.Max(tableWidth, LogicalToDeviceUnits(480));
        _note.MaximumSize = new Size(width, 0);
        var height = _body.Padding.Vertical + _activeLabel.GetPreferredSize(Size.Empty).Height + tableHeight +
                     _note.GetPreferredSize(new Size(width, 0)).Height + _buttons.GetPreferredSize(Size.Empty).Height;

        WindowFit.Fit(this, new Size(width + _body.Padding.Horizontal, height), growOnly: _fitted);
        _fitted = true;
    }
}
