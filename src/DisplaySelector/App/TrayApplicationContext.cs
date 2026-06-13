using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using DisplaySelector.Core;
using DisplaySelector.Core.Audio;
using DisplaySelector.Core.Display;
using DisplaySelector.Core.Logging;
using DisplaySelector.Core.Profiles;
using DisplaySelector.UI;

namespace DisplaySelector.App;

/// <summary>
/// The controller: owns the tray icon + menu and wires services. M0 is the idle shell —
/// profile activation, hotkeys, audio/display services land in later milestones. Functional now:
/// load profiles/config, debug-logging toggle, open log folder, single-instance surface, exit.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly FileLogger _logger;
    private readonly ILog _log;
    private readonly IProfileStore _profileStore;
    private readonly IConfigStore _configStore;
    private readonly IAudioService _audioService;
    private readonly IDisplayService _displayService;
    private readonly HiddenWindow _listener;
    private readonly NotifyIcon _tray;

    private AppConfig _config;
    private ProfilesDocument _document;

    public TrayApplicationContext(
        FileLogger logger,
        IProfileStore profileStore,
        IConfigStore configStore,
        IAudioService audioService,
        IDisplayService displayService,
        uint surfaceMessage)
    {
        _logger = logger;
        _log = logger;
        _profileStore = profileStore;
        _configStore = configStore;
        _audioService = audioService;
        _displayService = displayService;

        _config = _configStore.Load();
        _logger.Level = _config.DebugLogging ? LogLevel.Debug : LogLevel.Info;
        _document = _profileStore.Load();

        _listener = new HiddenWindow(surfaceMessage);
        _listener.MessageReceived += OnSurfaceRequested;

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application, // TODO(M4): replace with assets/icon.ico
            Visible = true,
            Text = "Display Selector",
            ContextMenuStrip = BuildMenu(),
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowMenu();
            }
        };

        _log.Info("Tray application started (idle shell).");
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add(new ToolStripMenuItem("Display Selector") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());

        if (_document.Profiles.Count == 0)
        {
            menu.Items.Add(new ToolStripMenuItem("(no profiles yet)") { Enabled = false });
        }
        else
        {
            // Activation wired in M3; listed here so the menu reflects stored state.
            foreach (var p in _document.Profiles)
            {
                menu.Items.Add(new ToolStripMenuItem(p.Name) { Enabled = false });
            }
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Stub("Save current settings as new profile…", "M3"));

        var diagnostics = new ToolStripMenuItem("Diagnostics");
        var debugToggle = new ToolStripMenuItem("Enable debug logging")
        {
            Checked = _config.DebugLogging,
            CheckOnClick = true,
        };
        debugToggle.Click += (_, _) => ToggleDebugLogging(debugToggle.Checked);
        diagnostics.DropDownItems.Add(debugToggle);

        var audioTest = new ToolStripMenuItem("Run audio test…");
        audioTest.Click += (_, _) => RunAudioTest();
        diagnostics.DropDownItems.Add(audioTest);

        var displayTest = new ToolStripMenuItem("Run display test…");
        displayTest.Click += (_, _) => RunDisplayTest();
        diagnostics.DropDownItems.Add(displayTest);

        var openLogs = new ToolStripMenuItem("Open log folder");
        openLogs.Click += (_, _) => OpenLogFolder();
        diagnostics.DropDownItems.Add(openLogs);
        menu.Items.Add(diagnostics);

        menu.Items.Add(Stub("Start with Windows", "M4"));

        menu.Items.Add(new ToolStripSeparator());

        var about = new ToolStripMenuItem("About");
        about.Click += (_, _) => ShowAbout();
        menu.Items.Add(about);

        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => ExitApp();
        menu.Items.Add(exit);

        return menu;
    }

    private ToolStripMenuItem Stub(string text, string milestone)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) =>
        {
            _log.Info($"Stub invoked: '{text}' (planned {milestone}).");
            _tray.ShowBalloonTip(2000, "Display Selector", $"'{text}' is coming in {milestone}.", ToolTipIcon.Info);
        };
        return item;
    }

    private void ToggleDebugLogging(bool enabled)
    {
        _logger.Level = enabled ? LogLevel.Debug : LogLevel.Info;
        _config.DebugLogging = enabled;
        _configStore.Save(_config);
        _log.Info($"Debug logging {(enabled ? "enabled" : "disabled")}.");
        _tray.ShowBalloonTip(2000, "Display Selector", $"Debug logging {(enabled ? "enabled" : "disabled")}.", ToolTipIcon.Info);
    }

    private void RunAudioTest()
    {
        _log.Info("Opening audio test dialog.");
        using var dialog = new AudioTestDialog(_audioService, _log);
        dialog.ShowDialog();
    }

    private void RunDisplayTest()
    {
        _log.Info("Opening display test dialog.");
        using var dialog = new DisplayTestDialog(_displayService, _log);
        dialog.ShowDialog();
    }

    private void OpenLogFolder()
    {
        Directory.CreateDirectory(AppPaths.LogsDirectory);
        Process.Start(new ProcessStartInfo { FileName = AppPaths.LogsDirectory, UseShellExecute = true });
        _log.Info("Opened log folder.");
    }

    private void ShowAbout()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";
        _tray.ShowBalloonTip(
            3000,
            "Display Selector",
            $"Version {version} — switch display + audio profiles with a hotkey.",
            ToolTipIcon.Info);
    }

    private void OnSurfaceRequested()
    {
        _log.Info("Second instance launched; surfacing menu.");
        _tray.ShowBalloonTip(1500, "Display Selector", "Already running.", ToolTipIcon.Info);
        ShowMenu();
    }

    private void ShowMenu()
    {
        // Reuse NotifyIcon's own context-menu display (handles foreground/dismissal correctly).
        var showContextMenu = typeof(NotifyIcon).GetMethod(
            "ShowContextMenu",
            BindingFlags.Instance | BindingFlags.NonPublic);
        showContextMenu?.Invoke(_tray, null);
    }

    private void ExitApp()
    {
        _log.Info("Exit requested; shutting down.");
        _tray.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tray.Dispose();
            _listener.Dispose();
        }

        base.Dispose(disposing);
    }
}
