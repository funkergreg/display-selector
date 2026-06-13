using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using DisplaySelector.Core;
using DisplaySelector.Core.Activation;
using DisplaySelector.Core.Audio;
using DisplaySelector.Core.Display;
using DisplaySelector.Core.Hotkeys;
using DisplaySelector.Core.Logging;
using DisplaySelector.Core.Profiles;
using DisplaySelector.UI;

namespace DisplaySelector.App;

/// <summary>
/// The controller: owns the tray icon + menu and wires the services. Handles profile CRUD, hotkey
/// registration, and activation (delegated to <see cref="ProfileActivator"/>). Every command is
/// reachable here; hotkeys are accelerators only.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private static readonly string[] DefaultHotkeyKeys = { "F9", "F10", "F11", "F12" };

    private readonly FileLogger _logger;
    private readonly ILog _log;
    private readonly IProfileStore _profileStore;
    private readonly IConfigStore _configStore;
    private readonly IAudioService _audioService;
    private readonly IDisplayService _displayService;
    private readonly IHotkeyService _hotkeyService;
    private readonly ProfileActivator _activator;
    private readonly HiddenWindow _listener;
    private readonly NotifyIcon _tray;

    private readonly Dictionary<int, string> _hotkeyIdToProfileId = new();

    private AppConfig _config;
    private ProfilesDocument _document;
    private int _nextHotkeyId = 1;

    public TrayApplicationContext(
        FileLogger logger,
        IProfileStore profileStore,
        IConfigStore configStore,
        IAudioService audioService,
        IDisplayService displayService,
        IHotkeyService hotkeyService,
        ProfileActivator activator,
        uint surfaceMessage)
    {
        _logger = logger;
        _log = logger;
        _profileStore = profileStore;
        _configStore = configStore;
        _audioService = audioService;
        _displayService = displayService;
        _hotkeyService = hotkeyService;
        _activator = activator;

        _config = _configStore.Load();
        _logger.Level = _config.DebugLogging ? LogLevel.Debug : LogLevel.Info;
        _document = _profileStore.Load();

        _listener = new HiddenWindow(surfaceMessage);
        _listener.MessageReceived += OnSurfaceRequested;

        _hotkeyService.HotkeyPressed += OnHotkeyPressed;

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application, // TODO(M4): replace with assets/icon.ico
            Visible = true,
            Text = "Display Selector",
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowMenu();
            }
        };

        RegisterAllHotkeys();
        RebuildMenu();

        _log.Info($"Tray application started. {_document.Profiles.Count} profile(s) loaded.");
    }

    // ---- Menu ----------------------------------------------------------------------------------

    private void RebuildMenu()
    {
        var old = _tray.ContextMenuStrip;
        _tray.ContextMenuStrip = BuildMenu();
        old?.Dispose();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        var activeId = FindActiveProfileId();

        var activeName = activeId is null
            ? "Custom (unsaved)"
            : _document.Profiles.First(p => p.Id == activeId).Name;
        menu.Items.Add(new ToolStripMenuItem($"Active: {activeName}") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());

        if (_document.Profiles.Count == 0)
        {
            menu.Items.Add(new ToolStripMenuItem("(no profiles yet)") { Enabled = false });
        }
        else
        {
            foreach (var profile in _document.Profiles)
            {
                var label = profile.Hotkey is null
                    ? profile.Name
                    : $"{profile.Name} : {HotkeyCodec.Format(profile.Hotkey)}";
                var item = new ToolStripMenuItem(label)
                {
                    Checked = profile.Id == activeId,
                };
                var id = profile.Id;
                item.Click += (_, _) => ActivateProfile(id);
                menu.Items.Add(item);
            }
        }

        menu.Items.Add(new ToolStripSeparator());
        var save = new ToolStripMenuItem("Save current settings as new profile…");
        save.Click += (_, _) => SaveCurrentAsProfile();
        menu.Items.Add(save);

        var saveAudio = new ToolStripMenuItem("Save current audio device as profile…");
        saveAudio.Click += (_, _) => SaveCurrentAudioAsProfile();
        menu.Items.Add(saveAudio);

        menu.Items.Add(BuildManageMenu());

        menu.Items.Add(BuildDiagnosticsMenu());

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

    private ToolStripMenuItem BuildManageMenu()
    {
        var manage = new ToolStripMenuItem("Manage profiles");
        if (_document.Profiles.Count == 0)
        {
            manage.DropDownItems.Add(new ToolStripMenuItem("(no profiles yet)") { Enabled = false });
            return manage;
        }

        foreach (var profile in _document.Profiles)
        {
            var id = profile.Id;
            var sub = new ToolStripMenuItem(profile.Name);

            var rename = new ToolStripMenuItem("Rename…");
            rename.Click += (_, _) => RenameProfile(id);
            sub.DropDownItems.Add(rename);

            var setHotkey = new ToolStripMenuItem("Set hotkey…");
            setHotkey.Click += (_, _) => SetHotkey(id);
            sub.DropDownItems.Add(setHotkey);

            var setAudio = new ToolStripMenuItem("Set audio device…");
            setAudio.Click += (_, _) => SetProfileAudio(id);
            sub.DropDownItems.Add(setAudio);

            sub.DropDownItems.Add(new ToolStripSeparator());

            var delete = new ToolStripMenuItem("Delete…");
            delete.Click += (_, _) => DeleteProfile(id);
            sub.DropDownItems.Add(delete);

            manage.DropDownItems.Add(sub);
        }

        return manage;
    }

    private ToolStripMenuItem BuildDiagnosticsMenu()
    {
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

        return diagnostics;
    }

    private ToolStripMenuItem Stub(string text, string milestone)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) =>
        {
            _log.Info($"Stub invoked: '{text}' (planned {milestone}).");
            ShowBalloon($"'{text}' is coming in {milestone}.", ToolTipIcon.Info);
        };
        return item;
    }

    // ---- Profile operations --------------------------------------------------------------------

    private void ActivateProfile(string id)
    {
        var profile = _document.Profiles.FirstOrDefault(p => p.Id == id);
        if (profile is null)
        {
            return;
        }

        var result = _activator.Activate(profile);

        var heading = $"Switched to: {profile.Name}";
        var detail = result.Messages.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, result.Messages) : string.Empty;
        ShowBalloon(heading + detail, result.Success ? ToolTipIcon.Info : ToolTipIcon.Warning);

        _tray.Text = Truncate($"Display Selector — {profile.Name}", 63);
        RebuildMenu();
    }

    private void SaveCurrentAsProfile()
    {
        var name = TextInputDialog.Prompt("Save profile", "Name for this profile:");
        if (name is null)
        {
            return;
        }

        DisplayConfig? display = null;
        try
        {
            display = _displayService.Capture();
        }
        catch (Exception ex)
        {
            _log.Error("Failed to capture display configuration.", ex);
        }

        var defaultDevice = _audioService.GetDefaultOutputDevice();
        var audio = defaultDevice is null
            ? null
            : new AudioConfig { EndpointId = defaultDevice.Id, FriendlyName = defaultDevice.FriendlyName };

        var profile = new Profile
        {
            Name = name,
            Display = display,
            Audio = audio,
            Hotkey = PickDefaultHotkey(),
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        _document.Profiles.Add(profile);
        _profileStore.Save(_document);
        RegisterAllHotkeys();
        RebuildMenu();

        var hotkeyNote = profile.Hotkey is null ? string.Empty : $" ({HotkeyCodec.Format(profile.Hotkey)})";
        ShowBalloon($"Saved profile '{name}'{hotkeyNote}.", ToolTipIcon.Info);
    }

    private void SaveCurrentAudioAsProfile()
    {
        var device = _audioService.GetDefaultOutputDevice();
        if (device is null)
        {
            ShowBalloon("No default audio device to save.", ToolTipIcon.Warning);
            return;
        }

        var name = TextInputDialog.Prompt(
            "Save audio profile",
            $"Name for this audio-only profile (device: {device.FriendlyName}):");
        if (name is null)
        {
            return;
        }

        var profile = new Profile
        {
            Name = name,
            Display = null, // audio-only: leaves displays untouched on activation
            Audio = new AudioConfig { EndpointId = device.Id, FriendlyName = device.FriendlyName },
            Hotkey = PickDefaultHotkey(),
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        _document.Profiles.Add(profile);
        _profileStore.Save(_document);
        RegisterAllHotkeys();
        RebuildMenu();

        var hotkeyNote = profile.Hotkey is null ? string.Empty : $" ({HotkeyCodec.Format(profile.Hotkey)})";
        ShowBalloon($"Saved audio-only profile '{name}'{hotkeyNote}.", ToolTipIcon.Info);
    }

    private void SetProfileAudio(string id)
    {
        var profile = _document.Profiles.FirstOrDefault(p => p.Id == id);
        if (profile is null)
        {
            return;
        }

        var devices = _audioService.GetOutputDevices();
        if (devices.Count == 0)
        {
            ShowBalloon("No audio output devices found.", ToolTipIcon.Warning);
            return;
        }

        var current = devices.FirstOrDefault(d => d.Id == profile.Audio?.EndpointId);
        var chosen = ListPickerDialog<AudioEndpoint>.Pick(
            "Set audio device",
            $"Output device for '{profile.Name}':",
            devices,
            d => d.IsDefault ? $"{d.FriendlyName}  (default)" : d.FriendlyName,
            current);
        if (chosen is null)
        {
            return;
        }

        profile.Audio = new AudioConfig { EndpointId = chosen.Id, FriendlyName = chosen.FriendlyName };
        _profileStore.Save(_document);
        RebuildMenu();
        ShowBalloon($"Set audio for '{profile.Name}' to '{chosen.FriendlyName}'.", ToolTipIcon.Info);
    }

    private void AssignDeviceToProfile(AudioEndpoint endpoint)
    {
        if (_document.Profiles.Count == 0)
        {
            ShowBalloon("No profiles yet — save one first.", ToolTipIcon.Info);
            return;
        }

        var profile = ListPickerDialog<Profile>.Pick(
            "Assign to profile",
            $"Set '{endpoint.FriendlyName}' as the audio device for which profile?",
            _document.Profiles,
            p => p.Name);
        if (profile is null)
        {
            return;
        }

        profile.Audio = new AudioConfig { EndpointId = endpoint.Id, FriendlyName = endpoint.FriendlyName };
        _profileStore.Save(_document);
        RebuildMenu();
        ShowBalloon($"Set '{endpoint.FriendlyName}' on profile '{profile.Name}'.", ToolTipIcon.Info);
    }

    private void RenameProfile(string id)
    {
        var profile = _document.Profiles.FirstOrDefault(p => p.Id == id);
        if (profile is null)
        {
            return;
        }

        var name = TextInputDialog.Prompt("Rename profile", "New name:", profile.Name);
        if (name is null || name == profile.Name)
        {
            return;
        }

        var oldName = profile.Name;
        profile.Name = name;
        _profileStore.Save(_document);
        RebuildMenu();
        ShowBalloon($"Renamed '{oldName}' to '{name}'.", ToolTipIcon.Info);
    }

    private void DeleteProfile(string id)
    {
        var profile = _document.Profiles.FirstOrDefault(p => p.Id == id);
        if (profile is null)
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"Delete profile '{profile.Name}'? This cannot be undone.",
            "Delete profile",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.OK)
        {
            return;
        }

        _document.Profiles.Remove(profile);
        _profileStore.Save(_document);
        RegisterAllHotkeys();
        RebuildMenu();
        ShowBalloon($"Deleted profile '{profile.Name}'.", ToolTipIcon.Info);
    }

    private void SetHotkey(string id)
    {
        var profile = _document.Profiles.FirstOrDefault(p => p.Id == id);
        if (profile is null)
        {
            return;
        }

        using var dialog = new HotkeyCaptureDialog(profile.Hotkey);
        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        var binding = dialog.Binding;

        // Clear the hotkey.
        if (binding is null)
        {
            profile.Hotkey = null;
            _profileStore.Save(_document);
            RegisterAllHotkeys();
            RebuildMenu();
            ShowBalloon($"Cleared hotkey for '{profile.Name}'.", ToolTipIcon.Info);
            return;
        }

        if (HotkeyCodec.IsRisky(binding))
        {
            var proceed = MessageBox.Show(
                $"'{HotkeyCodec.Format(binding)}' has no modifier and may interfere with normal typing. Use it anyway?",
                "Risky hotkey",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);
            if (proceed != DialogResult.OK)
            {
                return;
            }
        }

        // Intra-app conflict: another profile already uses this combo.
        var clash = _document.Profiles.FirstOrDefault(
            p => p.Id != id && p.Hotkey is not null && HotkeyCodec.Format(p.Hotkey) == HotkeyCodec.Format(binding));
        if (clash is not null)
        {
            var reassign = MessageBox.Show(
                $"'{HotkeyCodec.Format(binding)}' is already assigned to '{clash.Name}'. Move it to '{profile.Name}'?",
                "Hotkey in use",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question);
            if (reassign != DialogResult.OK)
            {
                return;
            }

            clash.Hotkey = null;
        }

        profile.Hotkey = binding;
        _profileStore.Save(_document);

        // Re-register everything, then check this profile's hotkey actually took (external conflict).
        RegisterAllHotkeys();
        if (!_hotkeyIdToProfileId.ContainsValue(profile.Id))
        {
            MessageBox.Show(
                $"'{HotkeyCodec.Format(binding)}' could not be registered — another application is already using it. " +
                "The hotkey is saved but inactive; choose a different combination.",
                "Hotkey unavailable",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        RebuildMenu();
        ShowBalloon($"Set hotkey for '{profile.Name}' to {HotkeyCodec.Format(binding)}.", ToolTipIcon.Info);
    }

    // ---- Hotkeys -------------------------------------------------------------------------------

    private void RegisterAllHotkeys()
    {
        _hotkeyService.UnregisterAll();
        _hotkeyIdToProfileId.Clear();
        _nextHotkeyId = 1;

        foreach (var profile in _document.Profiles)
        {
            if (profile.Hotkey is null)
            {
                continue;
            }

            var hotkeyId = _nextHotkeyId++;
            if (_hotkeyService.TryRegister(hotkeyId, profile.Hotkey))
            {
                _hotkeyIdToProfileId[hotkeyId] = profile.Id;
            }
            else
            {
                _log.Info($"Hotkey for '{profile.Name}' ({HotkeyCodec.Format(profile.Hotkey)}) not registered (in use).");
            }
        }
    }

    private void OnHotkeyPressed(int hotkeyId)
    {
        // Raised on the UI thread (message-only window), so UI work here is safe.
        if (_hotkeyIdToProfileId.TryGetValue(hotkeyId, out var profileId))
        {
            ActivateProfile(profileId);
        }
    }

    private HotkeyBinding? PickDefaultHotkey()
    {
        var used = _document.Profiles
            .Where(p => p.Hotkey is not null)
            .Select(p => HotkeyCodec.Format(p.Hotkey))
            .ToHashSet();

        foreach (var key in DefaultHotkeyKeys)
        {
            var candidate = new HotkeyBinding { Key = key };
            if (!used.Contains(HotkeyCodec.Format(candidate)))
            {
                return candidate;
            }
        }

        return null;
    }

    // ---- Active-profile detection --------------------------------------------------------------

    private string? FindActiveProfileId()
    {
        var currentAudioId = _audioService.GetDefaultOutputDevice()?.Id;
        var currentDisplays = _displayService.GetCurrentDisplays();
        var currentKeys = currentDisplays.Select(d => d.StableId).OrderBy(x => x).ToList();
        var currentPrimary = currentDisplays.FirstOrDefault(d => d.Primary)?.StableId;

        foreach (var profile in _document.Profiles)
        {
            if (profile.Audio is { } audio && audio.EndpointId != currentAudioId)
            {
                continue;
            }

            if (profile.Display is { } display)
            {
                var keys = display.Targets.Select(t => t.StableId).OrderBy(x => x).ToList();
                var primary = display.Targets.FirstOrDefault(t => t.Primary)?.StableId;
                if (!keys.SequenceEqual(currentKeys) || primary != currentPrimary)
                {
                    continue;
                }
            }

            return profile.Id;
        }

        return null;
    }

    // ---- Diagnostics / misc --------------------------------------------------------------------

    private void ToggleDebugLogging(bool enabled)
    {
        _logger.Level = enabled ? LogLevel.Debug : LogLevel.Info;
        _config.DebugLogging = enabled;
        _configStore.Save(_config);
        _log.Info($"Debug logging {(enabled ? "enabled" : "disabled")}.");
        ShowBalloon($"Debug logging {(enabled ? "enabled" : "disabled")}.", ToolTipIcon.Info);
    }

    private void RunAudioTest()
    {
        _log.Info("Opening audio test dialog.");
        using var dialog = new AudioTestDialog(_audioService, _log, AssignDeviceToProfile);
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
        ShowBalloon($"Version {version} — switch display + audio profiles with a hotkey.", ToolTipIcon.Info);
    }

    private void OnSurfaceRequested()
    {
        _log.Info("Second instance launched; surfacing menu.");
        ShowBalloon("Already running.", ToolTipIcon.Info);
        ShowMenu();
    }

    private void ShowMenu()
    {
        var showContextMenu = typeof(NotifyIcon).GetMethod(
            "ShowContextMenu",
            BindingFlags.Instance | BindingFlags.NonPublic);
        showContextMenu?.Invoke(_tray, null);
    }

    private void ShowBalloon(string message, ToolTipIcon icon) =>
        _tray.ShowBalloonTip(2500, "Display Selector", message, icon);

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

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
