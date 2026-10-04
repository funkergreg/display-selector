using DisplaySelector.App;
using DisplaySelector.Core.Profiles;

namespace DisplaySelector.UI;

/// <summary>
/// The Profile Manager window: the tray's per-profile submenu as a window. Left, the profile list
/// (same labels and order as the tray menu, active profile check-marked); right, the commands for the
/// selected profile; bottom, the "save current" actions, "Create a Launcher for Profile…"
/// (executable first) and Windows Display Settings.
/// Modeless and single-instance; the controller calls <see cref="RefreshProfiles"/> after every change
/// from any source (this window, the tray menu, hotkeys, launchers) so it stays live.
/// </summary>
internal sealed class ProfileManagerForm : Form
{
    private const int GutterWidth = 22;

    private readonly IProfileActions _actions;
    private readonly ListBox _list = new()
    {
        Dock = DockStyle.Fill,
        IntegralHeight = false,
        DrawMode = DrawMode.OwnerDrawFixed,
    };
    private readonly SplitContainer _split = new()
    {
        Dock = DockStyle.Fill,
        FixedPanel = FixedPanel.Panel2,
    };
    private readonly FlowLayoutPanel _commands = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        Padding = new Padding(4, 8, 8, 8),
    };
    private readonly FlowLayoutPanel _bottom = new()
    {
        Dock = DockStyle.Bottom,
        AutoSize = true,
        FlowDirection = FlowDirection.LeftToRight,
        Padding = new Padding(8, 0, 8, 8),
    };
    private readonly List<Button> _profileButtons = new();
    private readonly Button _moveUp;
    private readonly Button _moveDown;

    // What's on screen: profiles in display order (empty when there are no profiles).
    private List<Profile> _profiles = new();
    private string? _activeId;
    private bool _refreshing; // RefreshProfiles is rebuilding the list: not the user selecting

    public ProfileManagerForm(IProfileActions actions)
    {
        _actions = actions;

        Text = "Profile Manager";
        Icon = AppIcon.Window;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(620, 380);
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
        };

        _list.DrawItem += DrawProfileRow;
        _list.SelectedIndexChanged += (_, _) =>
        {
            UpdateButtons();

            // Only a real selection moves Profile Diagnostics: a live refresh re-selects the same row, and
            // must not pull a diagnostics window opened from the tray back to this selection.
            if (!_refreshing)
            {
                _actions.SelectionChanged(SelectedId);
            }
        };
        _list.DoubleClick += (_, _) => RunOnSelected(_actions.Activate);
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                RunOnSelected(_actions.Activate);
                e.Handled = true;
            }
        };

        var listPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 8, 4, 8) };
        listPanel.Controls.Add(_list);
        _split.Panel1.Controls.Add(listPanel);

        // Right pane: the per-profile commands, in the tray submenu's order.
        AddProfileButton("Switch to", _actions.Activate);
        AddSpacer();
        AddProfileButton("Rename…", _actions.Rename);
        AddProfileButton("Set hotkey…", _actions.SetHotkey);
        AddProfileButton("Set audio device…", _actions.SetAudio);
        AddProfileButton("Create a Launcher…", _actions.CreateLauncher);
        AddProfileButton("Create a Profile Shortcut", _actions.CreateProfileShortcut);
        AddSpacer();
        _moveUp = AddProfileButton("Move up", id => _actions.Move(id, -1));
        _moveDown = AddProfileButton("Move down", id => _actions.Move(id, +1));
        AddSpacer();
        AddProfileButton("Show diagnostics", _actions.ShowDiagnostics);
        AddSpacer();
        AddProfileButton("Delete…", _actions.Delete);
        _split.Panel2.Controls.Add(_commands);

        // Bottom bar: actions that don't need a selected profile.
        _bottom.Controls.Add(BarButton("Save current settings as new Profile…", _actions.SaveCurrent));
        _bottom.Controls.Add(BarButton("Save current audio device as Profile…", _actions.SaveCurrentAudio));
        _bottom.Controls.Add(BarButton("Create a Launcher for Profile…", _actions.CreateLauncherAndAssign));
        _bottom.Controls.Add(BarButton("Display Settings…", _actions.OpenDisplaySettings));

        Controls.Add(_split);
        Controls.Add(_bottom);

        RefreshProfiles();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        LayoutPanes();
    }

    // Dragged onto a monitor with a different scale: WinForms rescales fonts and controls, but not the
    // owner-drawn row height or the pane split, so redo those once the rescale has finished.
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        BeginInvoke(LayoutPanes);
    }

    // Sizes everything from the current font/DPI: row height, equal-width command buttons, a window
    // wide enough for the bottom bar on one row, and the command pane fixed to its buttons while the
    // list takes the rest.
    private void LayoutPanes()
    {
        if (IsDisposed)
        {
            return;
        }

        _list.ItemHeight = ProfileGlyphs.RowHeight(_list);

        foreach (var button in _profileButtons)
        {
            button.MinimumSize = Size.Empty;
        }
        var buttonWidth = _profileButtons.Max(b => b.GetPreferredSize(Size.Empty).Width);
        foreach (var button in _profileButtons)
        {
            button.MinimumSize = new Size(buttonWidth, 0);
        }

        var paneWidth = buttonWidth + _commands.Padding.Horizontal + SystemInformation.GetVerticalScrollBarWidthForDpi(DeviceDpi);
        var listMinWidth = LogicalToDeviceUnits(200);
        var barWidth = _bottom.Padding.Horizontal +
            _bottom.Controls.Cast<Control>().Sum(c => c.GetPreferredSize(Size.Empty).Width + c.Margin.Horizontal);
        var minClient = new Size(
            Math.Max(barWidth, listMinWidth + _split.SplitterWidth + paneWidth),
            _commands.GetPreferredSize(Size.Empty).Height + _bottom.GetPreferredSize(Size.Empty).Height);

        MinimumSize = SizeFromClientSize(minClient);
        if (ClientSize.Width < minClient.Width || ClientSize.Height < minClient.Height)
        {
            ClientSize = new Size(Math.Max(ClientSize.Width, minClient.Width), Math.Max(ClientSize.Height, minClient.Height));
            PerformLayout();
        }

        // Relax the limits before moving the splitter, so no intermediate state is out of range.
        _split.Panel1MinSize = 0;
        _split.Panel2MinSize = 0;
        var available = Math.Max(0, _split.Width - _split.SplitterWidth);
        _split.SplitterDistance = Math.Max(0, available - paneWidth);
        _split.Panel2MinSize = Math.Min(paneWidth, available);
        _split.Panel1MinSize = Math.Min(listMinWidth, _split.SplitterDistance);
        _list.Invalidate();
    }

    /// <summary>Redraws the list from the current profiles, keeping the selection on the same profile.</summary>
    public void RefreshProfiles()
    {
        var profiles = _actions.Profiles;
        var newIds = profiles.Select(p => p.Id).ToList();
        var selected = ProfileListSelection.Next(_profiles.Select(p => p.Id).ToList(), SelectedId, newIds);

        _profiles = profiles.ToList();
        _activeId = _actions.ActiveProfileId;

        _refreshing = true;
        try
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            if (profiles.Count == 0)
            {
                _list.Items.Add(ProfileLabels.NoProfiles);
            }
            else
            {
                foreach (var profile in profiles)
                {
                    _list.Items.Add(ProfileLabels.Label(profile));
                }
            }
            _list.SelectedIndex = selected is null ? -1 : newIds.IndexOf(selected);
            _list.EndUpdate();
        }
        finally
        {
            _refreshing = false;
        }

        UpdateButtons();
    }

    private string? SelectedId =>
        _list.SelectedIndex >= 0 && _list.SelectedIndex < _profiles.Count ? _profiles[_list.SelectedIndex].Id : null;

    private void RunOnSelected(Action<string> action)
    {
        if (SelectedId is { } id)
        {
            action(id);
        }
    }

    private void UpdateButtons()
    {
        var index = SelectedId is null ? -1 : _list.SelectedIndex;
        foreach (var button in _profileButtons)
        {
            button.Enabled = index >= 0;
        }
        _moveUp.Enabled = index > 0;
        _moveDown.Enabled = index >= 0 && index < _profiles.Count - 1;
    }

    private Button AddProfileButton(string text, Action<string> action)
    {
        var button = new Button { Text = text, AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
        button.Click += (_, _) => RunOnSelected(action);
        _profileButtons.Add(button);
        _commands.Controls.Add(button);
        return button;
    }

    private void AddSpacer() =>
        _commands.Controls.Add(new Panel { Height = 8, Width = 1, Margin = Padding.Empty });

    private static Button BarButton(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
        button.Click += (_, _) => action();
        return button;
    }

    // Check-mark gutter for the active profile (as in the tray menu), the Profile's glyph, then the
    // shared label. Columns scale with the monitor's DPI.
    private void DrawProfileRow(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0)
        {
            return;
        }

        var bounds = e.Bounds;
        var gutter = LogicalToDeviceUnits(GutterWidth);
        if (_profiles.Count == 0)
        {
            e.Graphics.FillRectangle(SystemBrushes.Window, bounds); // never looks selectable
            ProfileGlyphs.DrawLabel(e, ProfileLabels.NoProfiles, gutter, SystemColors.GrayText);
            return;
        }

        e.DrawBackground();
        var profile = _profiles[e.Index];
        if (profile.Id == _activeId)
        {
            var selected = (e.State & DrawItemState.Selected) != 0;
            TextRenderer.DrawText(
                e.Graphics, "✓", e.Font, new Rectangle(bounds.X, bounds.Y, gutter, bounds.Height),
                selected ? SystemColors.HighlightText : SystemColors.WindowText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        ProfileGlyphs.DrawRow(_list, e, profile, _list.Items[e.Index].ToString() ?? string.Empty, gutter);
        e.DrawFocusRectangle();
    }
}
