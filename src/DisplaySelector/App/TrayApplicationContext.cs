using System.Diagnostics;
using System.Reflection;
using DisplaySelector.Core;
using DisplaySelector.Core.Activation;
using DisplaySelector.Core.Audio;
using DisplaySelector.Core.Display;
using DisplaySelector.Core.Hotkeys;
using DisplaySelector.Core.Launch;
using DisplaySelector.Core.Logging;
using DisplaySelector.Core.Notifications;
using DisplaySelector.Core.Profiles;
using DisplaySelector.Core.Startup;
using DisplaySelector.UI;

namespace DisplaySelector.App;

/// <summary>
/// The controller: owns the tray icon + menu and wires the services. Handles profile CRUD, hotkey
/// registration, and activation (delegated to <see cref="ProfileActivator"/>). Every command is
/// reachable here; hotkeys are accelerators only.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext, IProfileActions
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
    private readonly IAutoStartManager _autoStart;
    private readonly LaunchCoordinator _launchCoordinator;
    private readonly LauncherCreator _launcherCreator;
    private readonly ProfileShortcutCleanup _shortcutCleanup;
    private readonly AudioSwitchConfirmer _audioConfirmer;
    private readonly ReapplyTracker _reapply = new();
    private readonly INotificationService _notifications;
    private readonly HiddenWindow _listener;
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _trimTimer;

    private readonly Dictionary<int, string> _hotkeyIdToProfileId = new();

    private AppConfig _config;
    private ProfilesDocument _document;
    private int _nextHotkeyId = 1;
    private string? _activeProfileId;

    // Every window opened from the tray / manager is single-instance (re-invoking brings it forward).
    private readonly SingleInstanceWindow<AboutDialog> _aboutWindow = new();
    private readonly SingleInstanceWindow<ProfileManagerForm> _managerWindow = new();
    private readonly SingleInstanceWindow<AudioTestDialog> _audioTestWindow = new();
    private readonly SingleInstanceWindow<DisplayTestDialog> _displayTestWindow = new();
    private readonly IReadOnlyList<string> _startupArgs;
    private readonly LatestOperation _launch = new();
    private readonly LatestOperation _audioConfirm = new();

    public TrayApplicationContext(
        FileLogger logger,
        IProfileStore profileStore,
        IConfigStore configStore,
        IAudioService audioService,
        IDisplayService displayService,
        IHotkeyService hotkeyService,
        ProfileActivator activator,
        IAutoStartManager autoStart,
        LaunchCoordinator launchCoordinator,
        IShortcutWriter shortcutWriter,
        ShortcutRegistry shortcutRegistry,
        uint surfaceMessage,
        IReadOnlyList<string> startupArgs)
    {
        _logger = logger;
        _log = logger;
        _profileStore = profileStore;
        _configStore = configStore;
        _audioService = audioService;
        _displayService = displayService;
        _hotkeyService = hotkeyService;
        _activator = activator;
        _autoStart = autoStart;
        _launchCoordinator = launchCoordinator;
        _launcherCreator = new LauncherCreator(shortcutWriter, shortcutRegistry, logger, ShowBalloon, ReportFailure);
        _shortcutCleanup = new ProfileShortcutCleanup(shortcutRegistry, shortcutWriter, logger);
        _audioConfirmer = new AudioSwitchConfirmer(audioService, logger);
        _startupArgs = startupArgs;

        // No config file yet => fresh install. Enable auto-start by default (the app is useless when
        // not running in the tray); the user can turn it off from the menu thereafter.
        var firstRun = !File.Exists(AppPaths.ConfigFile);

        _config = _configStore.Load();
        _logger.Level = _config.DebugLogging ? LogLevel.Debug : LogLevel.Info;
        _document = _profileStore.Load();

        if (firstRun)
        {
            EnableAutoStartByDefault();
        }

        _listener = new HiddenWindow(surfaceMessage);
        _listener.MessageReceived += OnSurfaceRequested;
        _listener.CommandReceived += args => HandleCommandLine(args, "forwarded from a second launch");

        _hotkeyService.HotkeyPressed += OnHotkeyPressed;

        _tray = new NotifyIcon
        {
            Icon = AppIcon.Tray,
            Visible = true,
            Text = AppIdentity.AppName,
        };

        // Toasts replace the previous one (no queue lag); fall back to a tray balloon if unavailable.
        _notifications = new ToastNotificationService(_log, ShowBalloonRaw);
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowMenu();
            }
        };

        // Debounced working-set trim: bursts of activity (startup, activation, dialogs) restart the
        // countdown, so we compact once the UI settles rather than on every step. Ticks on the UI
        // thread once the message loop is running — including the post-startup trim armed just below.
        _trimTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _trimTimer.Tick += (_, _) =>
        {
            _trimTimer.Stop();
            MemoryTuning.TrimWorkingSet();
        };

        RegisterAllHotkeys();
        RebuildMenu();

        _log.Info($"Tray application started. {_document.Profiles.Count} profile(s) loaded.");
        TrimWorkingSetSoon();

        // Started by a game shortcut while not already running: run its command once the message loop
        // (and so the UI synchronization context the launch flow awaits on) is up.
        if (_startupArgs.Count > 0)
        {
            Application.Idle += RunStartupCommandOnce;
        }
    }

    private void RunStartupCommandOnce(object? sender, EventArgs e)
    {
        Application.Idle -= RunStartupCommandOnce;
        HandleCommandLine(_startupArgs, "startup");
    }

    // Arms (or re-arms) the debounced post-idle working-set trim.
    private void TrimWorkingSetSoon()
    {
        _trimTimer.Stop();
        _trimTimer.Start();
    }

    // ---- Menu ----------------------------------------------------------------------------------

    private void RebuildMenu()
    {
        _activeProfileId = FindActiveProfileId();
        var old = _tray.ContextMenuStrip;
        _tray.ContextMenuStrip = BuildMenu(_activeProfileId);
        old?.Dispose();

        // Every change path ends here, so the Profile Manager window stays live whatever the source.
        _managerWindow.Current?.RefreshProfiles();
    }

    // For background changes (the audio confirm finishing): rebuild only if the active profile actually
    // changed, and never under an open menu, since rebuilding disposes it.
    private void RefreshIfActiveChanged()
    {
        if (_tray.ContextMenuStrip is not { Visible: true } && FindActiveProfileId() != _activeProfileId)
        {
            RebuildMenu();
        }
    }

    private ContextMenuStrip BuildMenu(string? activeId)
    {
        var menu = new ContextMenuStrip();

        var activeName = activeId is null
            ? "Custom (unsaved)"
            : _document.Profiles.First(p => p.Id == activeId).Name;
        menu.Items.Add(new ToolStripMenuItem($"Active: {activeName}") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());

        if (_document.Profiles.Count == 0)
        {
            menu.Items.Add(new ToolStripMenuItem(ProfileLabels.NoProfiles) { Enabled = false });
        }
        else
        {
            foreach (var profile in _document.Profiles)
            {
                var item = new ToolStripMenuItem(ProfileLabels.Label(profile))
                {
                    Checked = profile.Id == activeId,
                };
                var id = profile.Id;
                item.Click += (_, _) => SwitchToProfile(id);
                menu.Items.Add(item);
            }
        }

        menu.Items.Add(new ToolStripSeparator());
        var save = new ToolStripMenuItem("Save current settings as new Profile…");
        save.Click += (_, _) => SaveCurrentAsProfile();
        menu.Items.Add(save);

        var saveAudio = new ToolStripMenuItem("Save current audio device as Profile…");
        saveAudio.Click += (_, _) => SaveCurrentAudioAsProfile();
        menu.Items.Add(saveAudio);

        menu.Items.Add(BuildManageMenu());

        // Top-level (not just diagnostics): it's also the entry point for assigning an audio device
        // to an existing profile.
        var audioTest = new ToolStripMenuItem("Run audio test…");
        audioTest.Click += (_, _) => RunAudioTest();
        menu.Items.Add(audioTest);

        menu.Items.Add(BuildDiagnosticsMenu());

        var startup = new ToolStripMenuItem("Start with Windows")
        {
            Checked = _autoStart.IsEnabled(),
            CheckOnClick = true,
        };
        startup.Click += (_, _) => ToggleAutoStart(startup.Checked);
        menu.Items.Add(startup);

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
        // Clicking the parent opens the window; hovering still opens the submenu (minimal tray workflow).
        var manage = new ToolStripMenuItem("Manage Profiles");
        manage.Click += (_, _) =>
        {
            _tray.ContextMenuStrip?.Close();
            ShowProfileManager();
        };

        var openManager = new ToolStripMenuItem("Open Profile Manager…");
        openManager.Click += (_, _) => ShowProfileManager();
        manage.DropDownItems.Add(openManager);
        manage.DropDownItems.Add(new ToolStripSeparator());

        if (_document.Profiles.Count == 0)
        {
            manage.DropDownItems.Add(new ToolStripMenuItem(ProfileLabels.NoProfiles) { Enabled = false });
            return manage;
        }

        // Index-based loop so "Move up/down" enablement is O(1) per item instead of an O(n) IndexOf.
        for (var index = 0; index < _document.Profiles.Count; index++)
        {
            var profile = _document.Profiles[index];
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

            var createLauncher = new ToolStripMenuItem("Create a Launcher…");
            createLauncher.Click += (_, _) => CreateLauncher(id);
            sub.DropDownItems.Add(createLauncher);

            var createShortcut = new ToolStripMenuItem("Create a Profile Shortcut");
            createShortcut.Click += (_, _) => CreateProfileShortcut(id);
            sub.DropDownItems.Add(createShortcut);

            sub.DropDownItems.Add(new ToolStripSeparator());

            var moveUp = new ToolStripMenuItem("Move up") { Enabled = index > 0 };
            moveUp.Click += (_, _) => MoveProfile(id, -1);
            sub.DropDownItems.Add(moveUp);

            var moveDown = new ToolStripMenuItem("Move down") { Enabled = index < _document.Profiles.Count - 1 };
            moveDown.Click += (_, _) => MoveProfile(id, +1);
            sub.DropDownItems.Add(moveDown);

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
        // Broader label than "Diagnostics": this submenu now also holds bug-report / feature-request,
        // and it keeps the header visually distinct from the "Copy diagnostics" item below.
        // "and" not "&": a single "&" is a mnemonic prefix in menu text and would not render.
        var diagnostics = new ToolStripMenuItem("Help and diagnostics");

        var displayTest = new ToolStripMenuItem("Run display test…");
        displayTest.Click += (_, _) => RunDisplayTest();
        diagnostics.DropDownItems.Add(displayTest);

        var copyDiagnostics = new ToolStripMenuItem("Copy diagnostics");
        copyDiagnostics.Click += (_, _) => CopyDiagnostics();
        diagnostics.DropDownItems.Add(copyDiagnostics);

        var openLogs = new ToolStripMenuItem("Open log folder");
        openLogs.Click += (_, _) => OpenLogFolder();
        diagnostics.DropDownItems.Add(openLogs);

        var debugToggle = new ToolStripMenuItem("Enable debug logging")
        {
            Checked = _config.DebugLogging,
            CheckOnClick = true,
        };
        debugToggle.Click += (_, _) => ToggleDebugLogging(debugToggle.Checked);
        diagnostics.DropDownItems.Add(debugToggle);

        diagnostics.DropDownItems.Add(new ToolStripSeparator());

        var bugReport = new ToolStripMenuItem("Submit bug report…");
        bugReport.Click += (_, _) => SubmitBugReport();
        diagnostics.DropDownItems.Add(bugReport);

        var featureRequest = new ToolStripMenuItem("Request a feature…");
        featureRequest.Click += (_, _) => RequestFeature();
        diagnostics.DropDownItems.Add(featureRequest);

        return diagnostics;
    }

    // ---- Profile operations --------------------------------------------------------------------

    private Profile? FindProfile(string id) => _document.Profiles.FirstOrDefault(p => p.Id == id);

    // Persist the document and refresh the tray. Re-registers global hotkeys only when the change may
    // have affected them (a profile was added or removed); a rename/reorder/audio edit does not.
    private void PersistAndRefresh(bool reregisterHotkeys = false)
    {
        _profileStore.Save(_document);
        if (reregisterHotkeys)
        {
            RegisterAllHotkeys();
        }
        RebuildMenu();
    }

    // Hotkey, menu, Profile Manager and Profile Shortcut switches. A profile that's already live is
    // skipped; asking again within a few seconds forces the re-apply (the unstick fix).
    private ActivationResult? SwitchToProfile(string id)
    {
        if (FindProfile(id) is not { } profile)
        {
            return null;
        }

        var result = ApplyProfile(profile, force: _reapply.ShouldForce(id));
        if (result.AlreadyActive)
        {
            _reapply.RecordSkip(id);
            ShowBalloon($"Already on: {profile.Name}. Switch to it again to re-apply.", ToolTipIcon.Info);
        }
        else
        {
            _reapply.Reset();
        }
        return result;
    }

    // Applies a profile (skipped when it's already live and not forced) and surfaces it: toast, tray
    // text, menu, then the async audio confirm. Launchers call this directly: they never force, and
    // their outcome toast covers an already-live profile.
    private ActivationResult ApplyProfile(Profile profile, bool force)
    {
        var result = _activator.Activate(profile, force);
        if (!result.AlreadyActive)
        {
            _audioConfirm.Cancel(); // the previous profile's confirm must not outlive this switch
            ShowBalloon(
                string.Join(Environment.NewLine, ResultLines(result).Prepend($"Switched to: {profile.Name}")),
                result.Success ? ToolTipIcon.Info : ToolTipIcon.Warning);
            _tray.Text = Truncate($"Display-Selector — {profile.Name}", 63);
        }

        RebuildMenu();
        TrimWorkingSetSoon();
        if (result.AudioToConfirm is { } audio)
        {
            ConfirmAudio(audio);
        }
        return result;
    }

    // What an activation has to tell the user beyond "Switched to": missing displays, then failures.
    private static IEnumerable<string> ResultLines(ActivationResult result)
    {
        if (result.UnavailableDisplays.Count > 0)
        {
            yield return $"Displays not available: {string.Join(", ", result.UnavailableDisplays.Select(t => t.Friendly))}";
        }

        foreach (var message in result.Messages)
        {
            yield return message;
        }
    }

    // Waits (without blocking the UI) for the audio device to be ready and the default, plays the tone
    // on it, and warns only if it never got there. The newest switch wins.
    private void ConfirmAudio(AudioConfig audio) =>
        RunLatest(
            _audioConfirm,
            async token =>
            {
                var outcome = await _audioConfirmer.ConfirmAsync(audio, token);
                if (outcome == AudioConfirmOutcome.NotAvailable)
                {
                    ShowBalloon($"Audio device '{audio.FriendlyName}' isn't available. Turn it on, then switch again.", ToolTipIcon.Warning);
                }
                else if (outcome == AudioConfirmOutcome.NotDefault)
                {
                    ShowBalloon($"Audio device '{audio.FriendlyName}' couldn't stay the default device.", ToolTipIcon.Warning);
                }

                RefreshIfActiveChanged(); // devices may have come and gone meanwhile
            },
            "confirm the audio device");

    // async void: a UI entry point, so every exception is caught here. Starting work in a slot cancels
    // the work still running there (newest wins); a cancelled run just stops.
    private async void RunLatest(
        LatestOperation slot,
        Func<CancellationToken, Task> work,
        string failureAction,
        Action? onCancelled = null)
    {
        var operation = slot.Start();
        try
        {
            await work(operation.Token);
        }
        catch (OperationCanceledException)
        {
            onCancelled?.Invoke();
        }
        catch (Exception ex)
        {
            ReportFailure(failureAction, ex);
        }
        finally
        {
            slot.End(operation);
            TrimWorkingSetSoon();
        }
    }

    // Lowest-numbered "profile-N" not already in use, so the suggested name iterates automatically.
    private string NextDefaultProfileName()
    {
        for (var n = 1; ; n++)
        {
            var candidate = $"profile-{n}";
            if (!NameInUse(candidate))
            {
                return candidate;
            }
        }
    }

    // Case-insensitive name-collision check — a profile's name is its identity in the tray/menu UI,
    // so we keep names unique. Excludes a given id so renaming a profile to its own name is allowed.
    private bool NameInUse(string name, string? excludeId = null) =>
        _document.Profiles.Any(p => p.Id != excludeId &&
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    // Prompts for a name, refusing and re-prompting any name already taken by another profile.
    // Returns the accepted name, or null if the user cancelled.
    private string? PromptForUniqueName(string title, string prompt, string defaultName, string? excludeId = null)
    {
        var seed = defaultName;
        while (true)
        {
            var name = TextInputDialog.Prompt(title, prompt, initialValue: seed, placeholder: defaultName);
            if (name is null)
            {
                return null;
            }
            if (!NameInUse(name, excludeId))
            {
                return name;
            }
            ShowBalloon($"A Profile named '{name}' already exists — choose another name.", ToolTipIcon.Warning);
            seed = name; // reopen with what they typed so they can tweak it rather than start over
        }
    }

    // Confirmation prompt before saving a capture identical to an existing profile. The caller passes
    // the description of what was actually compared (e.g. "display and audio device configuration",
    // "display configuration", or "audio device configuration").
    private bool ConfirmDuplicateCapture(string matchName, string configDescription) =>
        MessageBox.Show(
            $"This {configDescription} already matches Profile '{matchName}'. Save another copy anyway?",
            "Duplicate configuration",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning) == DialogResult.OK;

    private void SaveCurrentAsProfile()
    {
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

        // Only dedupe when we actually captured a display — a failed capture leaves display null,
        // which would otherwise falsely match an audio-only profile with the same default device.
        var duplicate = display is null ? null : ProfileMatching.FindDuplicate(_document.Profiles, display, audio);
        if (duplicate is not null && !ConfirmDuplicateCapture(
                duplicate.Name,
                audio is null ? "display configuration" : "display and audio device configuration"))
        {
            return;
        }

        var name = PromptForUniqueName("Save Profile", "Name for this Profile:", NextDefaultProfileName());
        if (name is null)
        {
            return;
        }

        AddAndSaveProfile(name, display, audio, "Profile");
    }

    // Shared tail of the two "Save current …" flows: build the profile, persist, re-register hotkeys,
    // rebuild the menu, and notify.
    private void AddAndSaveProfile(string name, DisplayConfig? display, AudioConfig? audio, string savedNoun)
    {
        var profile = new Profile
        {
            Name = name,
            Display = display,
            Audio = audio,
            Hotkey = PickDefaultHotkey(),
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        _document.Profiles.Add(profile);
        PersistAndRefresh(reregisterHotkeys: true);

        var hotkeyNote = profile.Hotkey is null ? string.Empty : $" ({HotkeyCodec.Format(profile.Hotkey)})";
        _log.Info($"Saved {savedNoun} '{name}'{hotkeyNote}.");
        ShowBalloon($"Saved {savedNoun} '{name}'{hotkeyNote}.", ToolTipIcon.Info);
    }

    private void SaveCurrentAudioAsProfile()
    {
        var device = _audioService.GetDefaultOutputDevice();
        if (device is null)
        {
            ShowBalloon("No default audio device to save.", ToolTipIcon.Warning);
            return;
        }

        var audio = new AudioConfig { EndpointId = device.Id, FriendlyName = device.FriendlyName };

        var duplicate = ProfileMatching.FindDuplicate(_document.Profiles, null, audio);
        if (duplicate is not null &&
            !ConfirmDuplicateCapture(duplicate.Name, "audio device configuration"))
        {
            return;
        }

        var name = PromptForUniqueName(
            "Save audio Profile",
            $"Name for this audio-only Profile (device: {device.FriendlyName}):",
            NextDefaultProfileName());
        if (name is null)
        {
            return;
        }

        // Display null: audio-only profile leaves displays untouched on activation.
        AddAndSaveProfile(name, display: null, audio, "audio-only Profile");
    }

    private void SetProfileAudio(string id)
    {
        var profile = FindProfile(id);
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
            d => d.DisplayLabel,
            current);
        if (chosen is null)
        {
            return;
        }

        SetProfileAudio(profile, chosen);
    }

    // Saves the profile's new audio device (Set audio device…, or Assign to Profile… from the Audio
    // test). If the profile is the active one, the device is switched to straight away (with the
    // confirmation tone), so editing the active profile keeps it active instead of leaving the live
    // default behind. Choosing the device that's already the default changes nothing, so no tone.
    private void SetProfileAudio(Profile profile, AudioEndpoint device)
    {
        var switching = FindActiveProfileId() == profile.Id && _audioService.GetDefaultOutputDevice()?.Id != device.Id;
        profile.Audio = new AudioConfig { EndpointId = device.Id, FriendlyName = device.FriendlyName };
        if (switching)
        {
            _audioService.SetDefaultOutputDevice(device.Id);
        }

        _log.Info($"Set audio device for '{profile.Name}' to '{device.FriendlyName}'{(switching ? " and switched to it" : string.Empty)}.");
        PersistAndRefresh();
        ShowBalloon(
            $"Set audio for '{profile.Name}' to '{device.FriendlyName}'{(switching ? " and switched to it" : string.Empty)}.",
            ToolTipIcon.Info);
        if (switching)
        {
            ConfirmAudio(profile.Audio);
        }
    }

    private void AssignDeviceToProfile(AudioEndpoint endpoint)
    {
        var profile = ProfileLabels.Pick(
            "Assign to Profile",
            $"Set '{endpoint.FriendlyName}' as the audio device for which Profile?",
            _document.Profiles,
            ShowBalloon);
        if (profile is not null)
        {
            SetProfileAudio(profile, endpoint);
        }
    }

    private void MoveProfile(string id, int delta)
    {
        var index = _document.Profiles.FindIndex(p => p.Id == id);
        if (index < 0)
        {
            return;
        }

        var newIndex = index + delta;
        if (newIndex < 0 || newIndex >= _document.Profiles.Count)
        {
            return;
        }

        var profile = _document.Profiles[index];
        _document.Profiles.RemoveAt(index);
        _document.Profiles.Insert(newIndex, profile);
        _log.Info($"Moved profile '{profile.Name}' {(delta < 0 ? "up" : "down")}.");
        PersistAndRefresh();
    }

    private void RenameProfile(string id)
    {
        var profile = FindProfile(id);
        if (profile is null)
        {
            return;
        }

        var name = PromptForUniqueName("Rename Profile", "New name:", profile.Name, excludeId: id);
        if (name is null || name == profile.Name)
        {
            return;
        }

        var oldName = profile.Name;
        profile.Name = name;
        _log.Info($"Renamed profile '{oldName}' to '{name}'.");
        PersistAndRefresh();
        ShowBalloon($"Renamed '{oldName}' to '{name}'.", ToolTipIcon.Info);
    }

    private void DeleteProfile(string id)
    {
        var profile = FindProfile(id);
        if (profile is null)
        {
            return;
        }

        // Launchers / Profile Shortcuts the app made for this profile: offer to remove them too rather
        // than leave orphans on the desktop. Found before the profile is removed (they resolve to it).
        var linked = _shortcutCleanup.Find(profile, _document.Profiles);
        var question = $"Delete Profile '{profile.Name}'? This cannot be undone.";
        if (linked.Count > 0)
        {
            var items = string.Join(Environment.NewLine, linked.Select(path => $"  •  {Path.GetFileNameWithoutExtension(path)}"));
            question += $"{Environment.NewLine}{Environment.NewLine}These desktop items use it:{Environment.NewLine}{items}" +
                        $"{Environment.NewLine}{Environment.NewLine}Delete them too?";
        }

        // OK (no desktop items) or No keeps the shortcuts; only Yes deletes them.
        var answer = MessageBox.Show(
            question,
            "Delete Profile",
            linked.Count > 0 ? MessageBoxButtons.YesNoCancel : MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);
        if (answer == DialogResult.Cancel)
        {
            return;
        }

        _document.Profiles.Remove(profile);
        _log.Info($"Deleted profile '{profile.Name}'.");
        PersistAndRefresh(reregisterHotkeys: true);

        var removed = answer == DialogResult.Yes ? _shortcutCleanup.Delete(linked) : 0;
        ShowBalloon(
            removed > 0
                ? $"Deleted Profile '{profile.Name}' and {removed} desktop item{(removed == 1 ? string.Empty : "s")}."
                : $"Deleted Profile '{profile.Name}'.",
            ToolTipIcon.Info);
    }

    private void SetHotkey(string id)
    {
        var profile = FindProfile(id);
        if (profile is null)
        {
            return;
        }

        // Free our global hotkeys while capturing so the keypress reaches the dialog itself,
        // instead of an already-registered hotkey firing and activating another profile.
        _hotkeyService.UnregisterAll();
        _hotkeyIdToProfileId.Clear();

        string? balloon = null;
        try
        {
            using var dialog = new HotkeyCaptureDialog(profile.Hotkey);
            if (dialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            var binding = dialog.Binding;

            // Clear the hotkey. (Rebuild + notify inline: the shared bottom block is skipped by this return.)
            if (binding is null)
            {
                profile.Hotkey = null;
                _log.Info($"Cleared hotkey for '{profile.Name}'.");
                PersistAndRefresh();
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
            balloon = $"Set hotkey for '{profile.Name}' to {HotkeyCodec.Format(binding)}.";
        }
        finally
        {
            // Always restore registrations (including any new/changed binding).
            RegisterAllHotkeys();
        }

        if (balloon is null)
        {
            return; // cancelled or declined — nothing changed
        }

        // The just-assigned hotkey may have failed to register (owned by another app).
        if (profile.Hotkey is not null && !_hotkeyIdToProfileId.ContainsValue(profile.Id))
        {
            MessageBox.Show(
                $"'{HotkeyCodec.Format(profile.Hotkey)}' could not be registered — another application is already using it. " +
                "The hotkey is saved but inactive; choose a different combination.",
                "Hotkey unavailable",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        _log.Info(balloon);
        RebuildMenu();
        ShowBalloon(balloon, ToolTipIcon.Info);
    }

    // ---- Game shortcuts ------------------------------------------------------------------------

    // A command line at startup, or one forwarded by a second launch (a game shortcut double-clicked
    // while we're already running).
    private void HandleCommandLine(IReadOnlyList<string> args, string source)
    {
        _log.Info($"Command line ({source}): {string.Join(' ', args.Select(LaunchCommand.Quote))}");
        var command = LaunchCommand.TryParse(args, out var ignored);
        if (ignored.Count > 0)
        {
            _log.Info($"Ignoring unrecognized argument(s): {string.Join(' ', ignored.Select(LaunchCommand.Quote))}");
        }

        if (command is null)
        {
            ShowBalloon("Launcher not recognized — expected --profile <name> [--launch <target>].", ToolTipIcon.Warning);
            return;
        }

        RunLaunchCommand(command);
    }

    // The settle wait awaits on the UI thread, keeping the tray responsive while displays change.
    private void RunLaunchCommand(LaunchCommand command)
    {
        // A Profile Shortcut (no target) switches like a hotkey. A Launcher never forces a re-apply, and
        // its outcome toast covers an already-live profile.
        Func<Profile, ActivationResult?> activate = command.Target is null
            ? profile => SwitchToProfile(profile.Id)
            : profile => ApplyProfile(profile, force: false);

        // Newest command wins: a launch still waiting for displays to settle is cancelled.
        RunLatest(
            _launch,
            async token => ShowLaunchOutcome(await _launchCoordinator.RunAsync(command, _document.Profiles, activate, token)),
            "run the Launcher",
            () => _log.Info($"Pending launch of '{command.Target}' superseded by a newer shortcut command."));
    }

    // ApplyProfile already toasted "Switched to"; this replaces it with the combined result.
    private void ShowLaunchOutcome(LaunchOutcome outcome)
    {
        var command = outcome.Command;
        var targetName = command.TargetDisplayName;

        if (outcome.Profile is null)
        {
            var missing = $"This Launcher's Profile '{command.ProfileRef}' no longer exists";
            var message = !outcome.LaunchAttempted
                ? $"{missing}."
                : outcome.Launched
                    ? $"{missing} — launched {targetName} without switching."
                    : $"{missing}, and {targetName} couldn't be launched: {outcome.LaunchError}";
            ShowBalloon(message, ToolTipIcon.Warning);
            return;
        }

        if (!outcome.LaunchAttempted)
        {
            return; // Profile Shortcut: the activation toast is the whole story
        }

        if (outcome.Activation?.AlreadyActive == true)
        {
            ShowBalloon(
                outcome.Launched
                    ? $"Launched {targetName} (already on {outcome.Profile.Name})."
                    : $"{targetName} couldn't be launched: {outcome.LaunchError}",
                outcome.Launched ? ToolTipIcon.Info : ToolTipIcon.Warning);
            return;
        }

        var warn = outcome.Activation?.Success == false;
        var lines = new List<string>();
        if (outcome.Launched)
        {
            lines.Add($"Switched to: {outcome.Profile.Name} — launched {targetName}.");
            if (outcome.ReconnectedDisplays.Count > 0)
            {
                lines.Add($"{string.Join(", ", outcome.ReconnectedDisplays.Select(t => t.Friendly))} came on, and the Profile now uses it.");
            }

            if (outcome.Activation?.UnavailableDisplays.Count > 0)
            {
                warn = true; // still off when the wait gave up; the activation message below names them
            }
            else if (outcome.Settle.Outcome == SettleOutcome.TimedOut)
            {
                lines.Add("Displays were still changing when it started.");
                warn = true;
            }
        }
        else
        {
            lines.Add($"Switched to: {outcome.Profile.Name}, but {targetName} couldn't be launched: {outcome.LaunchError}");
            warn = true;
        }

        if (outcome.Activation is { } activation)
        {
            lines.AddRange(ResultLines(activation));
        }

        ShowBalloon(string.Join(Environment.NewLine, lines), warn ? ToolTipIcon.Warning : ToolTipIcon.Info);
    }

    // Per-profile launcher: file chooser → confirm → desktop launcher (no need to ask for the profile).
    private void CreateLauncher(string id)
    {
        if (FindProfile(id) is { } profile)
        {
            _launcherCreator.CreateForProfile(profile, LauncherDialogOwner);
        }
    }

    // Profile Shortcut: created straight away (no file chooser, no dialog); it just switches profiles.
    private void CreateProfileShortcut(string id)
    {
        if (FindProfile(id) is { } profile)
        {
            _launcherCreator.CreateProfileShortcut(profile, LauncherDialogOwner);
        }
    }

    // ---- Profile Manager window ----------------------------------------------------------------

    // Parent launcher dialogs to the manager only while it's on screen: Windows hides windows owned by
    // a minimized form, so a tray-started flow would otherwise open its file chooser invisibly.
    private IWin32Window? LauncherDialogOwner =>
        _managerWindow.Current is { Visible: true, WindowState: not FormWindowState.Minimized } manager ? manager : null;

    private void ShowProfileManager()
    {
        _log.Info("Opening Profile Manager window.");
        _managerWindow.ShowOrActivate(() => new ProfileManagerForm(this), modal: false, TrimWorkingSetSoon);
    }

    // IProfileActions: thin pass-throughs, so the window and the tray menu run identical operations.
    IReadOnlyList<Profile> IProfileActions.Profiles => _document.Profiles;

    string? IProfileActions.ActiveProfileId => _activeProfileId;

    void IProfileActions.Activate(string id) => SwitchToProfile(id);

    void IProfileActions.Rename(string id) => RenameProfile(id);

    void IProfileActions.SetHotkey(string id) => SetHotkey(id);

    void IProfileActions.SetAudio(string id) => SetProfileAudio(id);

    void IProfileActions.CreateLauncher(string id) => CreateLauncher(id);

    void IProfileActions.CreateProfileShortcut(string id) => CreateProfileShortcut(id);

    void IProfileActions.Move(string id, int delta) => MoveProfile(id, delta);

    void IProfileActions.Delete(string id) => DeleteProfile(id);

    void IProfileActions.SaveCurrent() => SaveCurrentAsProfile();

    void IProfileActions.SaveCurrentAudio() => SaveCurrentAudioAsProfile();

    void IProfileActions.CreateLauncherAndAssign() =>
        _launcherCreator.CreateAndAssign(_document.Profiles, LauncherDialogOwner);

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
            SwitchToProfile(profileId);
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
        return ProfileMatching.FindActive(_document.Profiles, currentDisplays, currentAudioId, _activator.IsLive)?.Id;
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
        _audioTestWindow.ShowOrActivate(
            () => new AudioTestDialog(_audioService, _log, AssignDeviceToProfile), modal: false, TrimWorkingSetSoon);
    }

    private void RunDisplayTest()
    {
        _log.Info("Opening display test dialog.");
        _displayTestWindow.ShowOrActivate(
            () => new DisplayTestDialog(_displayService, _log), modal: false, TrimWorkingSetSoon);
    }

    private void CopyDiagnostics()
    {
        try
        {
            var report = DiagnosticsReport.Build(_displayService, _audioService);
            Clipboard.SetText(report);
            _log.Info("Copied diagnostics to clipboard.");
            ShowBalloon("Diagnostics copied to clipboard.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            ReportFailure("copy diagnostics", ex);
        }
    }

    // Open a file, folder, or URL with the shell (default handler / browser).
    private static void OpenExternal(string target) =>
        Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });

    // Log an exception and surface a consistent "couldn't <action> — see the log" warning toast.
    private void ReportFailure(string action, Exception ex)
    {
        _log.Error($"Failed to {action}.", ex);
        ShowBalloon($"Couldn't {action} — see the log.", ToolTipIcon.Warning);
    }

    private void OpenLogFolder()
    {
        Directory.CreateDirectory(AppPaths.LogsDirectory);
        OpenExternal(AppPaths.LogsDirectory);
        _log.Info("Opened log folder.");
    }

    // Opens a prefilled GitHub bug report in the browser. GitHub can't attach files via URL, so the
    // system profile is inlined in the issue body and the (larger) recent log is placed on the
    // clipboard with the log folder opened — the reporter pastes the log or drags the file in.
    private void SubmitBugReport()
    {
        try
        {
            var diagnostics = IssueReporter.ScrubUser(DiagnosticsReport.Build(_displayService, _audioService));

            // Full (redacted) log goes to the clipboard; a short tail is inlined in the issue body
            // so there's runtime context even if the reporter never pastes the clipboard.
            var logTail = IssueReporter.ReadRecentLog();
            var url = IssueReporter.BugReportUrl(diagnostics, IssueReporter.TailLines(logTail));

            try
            {
                Clipboard.SetText(string.IsNullOrEmpty(logTail) ? " " : logTail);
            }
            catch (Exception ex)
            {
                _log.Error("Couldn't copy the log to the clipboard for the bug report.", ex);
            }

            Directory.CreateDirectory(AppPaths.LogsDirectory);
            OpenExternal(AppPaths.LogsDirectory);
            OpenExternal(url);

            _log.Info("Opened prefilled GitHub bug report; recent log copied to clipboard.");
            ShowBalloon(
                "Bug report opened in your browser. Your recent log is on the clipboard — paste it into the Logs section (or drag displayselector.log in from the folder that just opened).",
                ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            ReportFailure("open the bug report", ex);
        }
    }

    private void RequestFeature()
    {
        try
        {
            OpenExternal(IssueReporter.FeatureRequestUrl());
            _log.Info("Opened prefilled GitHub feature request.");
        }
        catch (Exception ex)
        {
            ReportFailure("open the feature request", ex);
        }
    }

    private void ShowAbout()
    {
        // AssemblyVersion is always 4-part (1.1.0.0); show the 3-part product version (1.1.0).
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
        _log.Info($"Showed About (version {version}).");
        _aboutWindow.ShowOrActivate(() => new AboutDialog(version, OpenAboutLink), modal: true, TrimWorkingSetSoon);
    }

    private void OpenAboutLink(string url)
    {
        try
        {
            OpenExternal(url);
            _log.Info($"Opened {url} from About.");
        }
        catch (Exception ex)
        {
            ReportFailure("open the link", ex);
        }
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

    // First-run default: turn on auto-start and persist it so this only happens once. On packaged
    // (MSIX) builds Enable() is a no-op and IsEnabled() stays false — the StartupTask handles it.
    private void EnableAutoStartByDefault()
    {
        _autoStart.Enable();
        _config.AutoStart = _autoStart.IsEnabled();
        _configStore.Save(_config);
        _log.Info($"First run: defaulted auto-start to {_config.AutoStart}.");
    }

    private void ToggleAutoStart(bool enabled)
    {
        if (enabled)
        {
            _autoStart.Enable();
        }
        else
        {
            _autoStart.Disable();
        }

        _config.AutoStart = _autoStart.IsEnabled();
        _configStore.Save(_config);
        _log.Info($"Auto-start {(_config.AutoStart ? "enabled" : "disabled")}.");
        ShowBalloon(
            _config.AutoStart ? "Display-Selector will start with Windows." : "Display-Selector will not start with Windows.",
            ToolTipIcon.Info);
    }

    // Shows a notification (toast, replacing the previous one), keeping the ToolTipIcon-based call sites.
    private void ShowBalloon(string message, ToolTipIcon icon) =>
        _notifications.Show(message, icon == ToolTipIcon.Warning ? NotificationLevel.Warning : NotificationLevel.Info);

    // The literal tray balloon — used only as the toast fallback.
    private void ShowBalloonRaw(string message, NotificationLevel level) =>
        _tray.ShowBalloonTip(2500, AppIdentity.AppName, message, level == NotificationLevel.Warning ? ToolTipIcon.Warning : ToolTipIcon.Info);

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
            Application.Idle -= RunStartupCommandOnce;
            _launch.Cancel();
            _audioConfirm.Cancel();
            _managerWindow.Close();
            _aboutWindow.Close();
            _audioTestWindow.Close();
            _displayTestWindow.Close();
            _trimTimer.Dispose();
            _tray.Dispose();
            _listener.Dispose();
        }

        base.Dispose(disposing);
    }
}
