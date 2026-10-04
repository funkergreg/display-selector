# CLAUDE.md — Display-Selector

Guidance for agentic coding on this repo. Keep this file current as the source of truth for *how to work here*; it must stay self-sufficient. README.md is the user-facing doc. The developer may create other markdown plans as scratch-pad working info; these are not to be committed.

## What this is

A lightweight, mostly-idle **Windows 11** system-tray utility (open-sourced). It captures the current **display + default audio device** as a named **Profile**, binds each **Profile** to a **global hotkey**, and switches the whole machine to a profile with one keypress. WinForms + .NET 10, distributed via an Inno Setup installer.

## Golden rules

- **Do NOT `git commit`, and do NOT `git push` — except** the release-publishing step explicitly outlined in the `/build-release` skill (creating a tagged GitHub release and uploading the installer). The developer does all routine commits/pushes as a QC step. You may always run read-only git (`status`, `diff`, `log`).
- **Windows 11 only** is the test target. Keep code portable (platform behind interfaces) but don't spend effort on other-OS/older-Windows support unless asked.
- **Platform code lives behind an interface.** Anything touching Win32/COM goes behind `IDisplayService` / `IAudioService` / `IHotkeyService` / `INotificationService` / `IAutoStartManager` / `IShortcutWriter` / `IProcessLauncher` / `ILog`, so it stays mockable and swappable.
- Prefer few dependencies. Current allowed set: **NAudio** (audio enumeration + WASAPI test playback) and **Microsoft.Toolkit.Uwp.Notifications** (Win11 toasts via the unpackaged compat layer). Logger is hand-rolled. Clear new dependencies with the developer first.
- Screenshots and other images in `.docs` are for staging and tests; these should NEVER be committed. Images used in the app or the actual documentation (README.md, etc.) go in root-level `assets/`. README screenshots are `assets/user-guide/ds-*.png`, cropped from the staged originals.

## Commands

- Build: `dotnet build`
- Run the tray app: `dotnet run --project src/DisplaySelector`
- Unit tests (headless, default loop): `dotnet test --filter "Category!=Integration"`
- Integration tests (real Windows APIs, non-destructive, needs a desktop session): `dotnet test --filter "Category=Integration"`
- Publish + package installer: `powershell -ExecutionPolicy Bypass -File build/build.ps1` (or `pwsh` if installed; flags: `-IncludeIntegration`, `-SkipTests`). Requires Inno Setup 6 for the installer step.
- Tier-3 physical checks (sound actually plays, displays actually switch) are **human-in-the-loop** via the app's in-tray menu, shortcuts, and UI windows — not automatable.

## Skills (prefer these over ad-hoc commands)

- **`/build-release`** — runs unit tests → publish → Inno Setup compile; reports the installer path.
- **`/run-tests`** — runs unit tests by default; integration on request; summarizes failures.
- Use built-in `/simplify`, `/security-review`, and `/code-review` on the diff since branching from `main` before telling the developer that a feature branch is ready to commit; do NOT run these on all changes, but DO run these if developer accepts results after human-in-the-loop testing or if the developer explicitly instructs to commit at any stage.

## Architecture (layered)

```text
Program.cs (single-instance Mutex, bootstrap)
  └─ TrayApplicationContext (controller: owns NotifyIcon + menu, wires services)
       ├─ IProfileStore   → JsonProfileStore  (atomic write + .bak, %LOCALAPPDATA%\DisplaySelector\)
       ├─ IDisplayService → CcdDisplayService (QueryDisplayConfig / SetDisplayConfig)
       ├─ IAudioService   → CoreAudioService  (Core Audio + IPolicyConfig, all 3 roles)
       ├─ IHotkeyService  → HotkeyService     (RegisterHotKey on a message-only window)
       ├─ INotificationService → toasts + tray balloon + confirmation tone
       ├─ IAutoStartManager → HKCU Run key
       ├─ LiveStateTracker (what's live: the active Profile for the check mark, tooltip, Profile Manager, plus the LiveSnapshot the live windows show; recomputed on Windows change events, see "Live state")
       ├─ LaunchCoordinator (game shortcuts: resolve profile → activate → DisplaySettleWaiter → IProcessLauncher)
       ├─ AudioSwitchConfirmer (after activation: waits for the audio device to be ready + default, re-asserts it, then plays the tone off the UI thread)
       ├─ LauncherCreator ("Create a Launcher…" flow: LauncherTargetPicker → [profile picker] → LauncherDetailsDialog → LauncherSpecBuilder → IShortcutWriter; "Create a Profile Shortcut" skips the picker and the dialog)
       ├─ ProfileShortcutCleanup (Delete Profile: finds the tracked .lnk files that point at it, offers to delete them)
       ├─ IShortcutWriter → ShellLinkShortcutWriter (IShellLinkW COM) + ShortcutRegistry (shortcuts.txt)
       ├─ ProfileManagerForm (Profile Manager window; drives the controller via IProfileActions, refreshed from RebuildMenu)
       ├─ DisplayTestDialog / ProfileDiagnosticsForm (live diagnostics windows; text from DisplayInventory / ProfileReport / DiagnosticsReport, so a display reads the same everywhere)
       └─ ILog → FileLogger (rolling, Info/Debug levels)
```

**Game shortcuts** (issue #4): a `.lnk` runs `DisplaySelector.exe --profile <id-or-name> [--launch <target>] [--args <args>]` (`LaunchCommand`). This command line is a **public contract**: shortcuts in the wild depend on it, so only add flags, never rename or remove them (unknown flags are ignored). A second launch with arguments forwards them to the running tray via `WM_COPYDATA` to `HiddenWindow` (`CommandChannel`). With no instance running, the launched process becomes the tray and runs the command after startup. Before launching, the tray waits (async, on the UI thread, 10 s timeout in `DisplaySettleWaiter`) for the applied display layout to settle. If a profile display wasn't connected at activation (a TV still off), the wait holds the launch open and re-activates the profile when it connects. Each display can trigger that once. The game always launches, even if the profile is missing, activation fails, or the wait times out. Each created `.lnk` path is recorded in `shortcuts.txt` (UTF-8 BOM). The Inno uninstaller deletes the recorded paths best-effort, and **Delete Profile** offers to delete the recorded ones that point at that profile (read back from each `.lnk`).

Profile **activation** is one orchestrated sequence in the controller: skip if the profile is already **live** → log → apply display → set default audio (all roles) → toast + tray update → log result → **confirm the audio asynchronously** (`AudioSwitchConfirmer`). Failures apply best-effort and are surfaced (toast) + logged.

- **Live state:** `LiveStateTracker.ActiveProfileId` is the one "what's live" answer (tray check mark + "Active:" header, tooltip, Profile Manager marker). It's a cached coarse match (`ProfileMatching.FindActive`: display set + primary + default audio), recomputed after the app's own changes and whenever Windows reports a change by anyone: `WM_DISPLAYCHANGE` and `WM_DEVICECHANGE`/`DBT_DEVNODES_CHANGED` (a display not in use plugged in or out) on `HiddenWindow` (top-level, so it gets the broadcasts) and `IMMNotificationClient` in `CoreAudioService` (`IAudioService.DefaultDeviceChanged`, raised on a COM worker thread, posted to the UI thread). A burst of events re-arms a 400 ms one-shot timer, so one switch costs one recompute. The menu's `Opening` re-checks as a safety net. **No polling**: nothing runs while idle. A change only updates marks in place (safe while the menu is open). Outside changes are silent (Info log only). Each recompute also refreshes an open Display Tester / Profile Diagnostics / Audio Tester. An unchanged view is left alone, so selection and scroll survive. Profile Diagnostics keeps the desktop items it found until the Profiles or shortcuts change, because a hardware event can't change `.lnk` files. Those windows show `LiveStateTracker.Snapshot` (`LiveSnapshot`) rather than querying Windows themselves, so they agree with the check mark and one refresh reads the hardware once. The snapshot reads the active displays and the default audio device up front. It reads the connected displays and the audio device list on first use, so an idle refresh costs no more. A failed refresh keeps the previous snapshot.
- **Skip when live:** every entry point (hotkey, menu, Profile Manager, Launcher, Profile Shortcut) first checks `ProfileActivator.IsLive`, fresh at switch time (never the tracker's cache): the default audio device plus the exact saved layout (`IDisplayService.MatchesCurrent`: displays, duplicate/extend, positions, resolution, rotation, refresh rate). This is stricter than the coarse check mark; when several profiles match coarsely, the check mark goes to the first live one, so it agrees with "Already on". Switches go through `SwitchToProfile` (repeat-to-force + toast); Launchers call `ApplyProfile` directly. A live profile isn't re-applied (toast "Already on"). Repeating the same profile within 5 s (`ReapplyTracker`) forces a full re-apply, which keeps the "unstick a frozen Windows display UI" fix. A Launcher never forces; with a live profile it launches at once.
- **Audio confirm:** never play the tone inline or on the UI thread. A TV's HDMI audio endpoint only exists while its display path is active, so right after a display switch it's still arriving. Playing on it stalled WASAPI and hung the app. The confirmer polls (about 10 s at most) until the device is active and the default, plays the tone (bounded, background thread), and re-asserts the default (max 3×) if Windows hands it to a newly arrived device. It warns only if the device never got ready. The newest activation cancels the previous confirm.

## Here be dragons (the two fragile areas — keep isolated, test hard)

1. **Display target matching across reboots/power cycles** (`CcdDisplayService`). Adapter LUIDs are **not** stable across reboots. Match targets **port-first** (`outputTechnology` + `connectorInstance`) with **EDID/monitorDevicePath fallback**; persist both. Hard hardware limit: displays that drop HDMI hot-plug-detect when powered off won't be reachable until powered on — handle best-effort + report, don't fight it. "Unavailable" means **not connected** (`QDC_ALL_PATHS` + `targetAvailable`), not merely inactive. Checking only active paths flags every display a profile is about to turn on (the TV when switching from the desk).
2. **`IPolicyConfig::SetDefaultEndpoint`** (`CoreAudioService`) is **undocumented** COM. Call it for all three roles (`eConsole`, `eMultimedia`, `eCommunications`) so every app + System Sounds follows. Keep all interop in one file behind `IAudioService` for easy replacement.

## Data & storage

- Location: `%LOCALAPPDATA%\DisplaySelector\` (non-roaming — profiles are hardware-specific). Files: `config.json`, `profiles.json` (+ `.bak`), `shortcuts.txt` (created game shortcuts, for uninstall), `logs\`.
- JSON, human-readable, with `schemaVersion` for migrations. Writes are atomic (tmp → `File.Replace`) with a retained `.bak`; corrupt/missing files recover from `.bak` or start empty (logged, never throw).
- Log full resolved settings on **save** and **activation**; at **Debug** level also log full serialized JSON + decoded display targets + audio endpoint IDs + API call traces (this is how data shape is debugged on a test machine).
- **Uninstall must purge everything** under `%LOCALAPPDATA%\DisplaySelector\` and remove the `Run` key. It also deletes the desktop shortcuts listed in `shortcuts.txt` best-effort, and it never fails on them. The Inno uninstaller handles all of this.
- **Start with Windows** is turned on by the app's first run (no `config.json` yet), not by the installer.
- **Redaction:** only **Submit bug report…** (`IssueReporter`) redacts: the Windows user name becomes `%USER%`, and the clipboard gets only the log's last ~6000 chars. **Copy diagnostics** and Profile Diagnostics' **Copy** are unredacted on purpose, because they're for the user's own troubleshooting. Launcher paths contain the user name, and the README warns about this. Keep that split, or update both docs if it changes.

## Conventions

- .NET 10 (`net10.0-windows10.0.19041.0` — the Windows-SDK TFM unlocks the WinRT toast projections), WinForms; nullable enabled; file-scoped namespaces; `async` only where it earns its keep (this app is mostly synchronous + event-driven).
- Every capability must be reachable from the tray menu (hotkeys are accelerators only). No silent state changes — every action gives visual feedback; the **confirmation tone is reserved for audio-device changes** and is the app's only sound: **toasts are silent** (`ToastNotificationService`; a chime would also play on the old device mid-switch). The balloon fallback can't be silenced (WinForms).
- Single instance enforced via a named `Mutex`. A second launch with no arguments surfaces the existing menu ("Already running.") and exits; with arguments it forwards them (see Game shortcuts). Left- and right-click on the tray icon both open the menu.
- **Hotkeys:** a new Profile gets the first free key of F9–F12, with no modifier (F-keys skip the "Risky hotkey" warning). Another app may already own a key, so `RegisterHotKey` fails. Surface that on save and on **Set hotkey…** (`IsHotkeyInactive`). At startup only log it, so a key another app always owns (Steam's F12) doesn't nag at every sign-in. Hotkeys are only deconflicted between Profiles, never against other apps.
- **Every window opened from the tray or Profile Manager is single-instance** via `SingleInstanceWindow<T>`. Profile Manager, Audio Tester, Display Tester and Profile Diagnostics (follows the Profile Manager's selection) are modeless (so you can save or switch with them open); About is modal. Re-invoking it brings the existing window to the front (restored if minimized) rather than opening a second copy. Short prompts within a command (`TextInputDialog`, `HotkeyCaptureDialog`, `ListPickerDialog`, `AudioDeviceSetterDialog`, `LauncherDetailsDialog`, message boxes) are exempt.
- **App terms are capitalized** in user-facing text (menus can't bold, so capitals mark them): **Profile**, **Launcher** (a desktop `.lnk` that switches to a Profile *and launches* a program) and **Profile Shortcut** (a `.lnk` that only switches). Ordinary words stay lowercase ("audio device", "hotkey"). Code identifiers (`ShortcutSpec`, `IShortcutWriter`, `shortcuts.txt`) and the `--profile/--launch/--args` contract keep their names.
- Profile labels (`Name : Hotkey` / `Name : No Hotkey`) come from `ProfileLabels.Label` everywhere, so the tray menu, the manager window and the pickers always match. Beside each label, `ProfileGlyphs` draws a blue monitor (full Profile) or a dark-vermillion speaker (audio-only, `Profile.IsAudioOnly`) from the Windows icon font: **shape carries the meaning, color only reinforces it** (Section 508), and both colors keep ≥ 4.5:1 contrast on white. On a selected row the glyph takes the highlight text color.
- Toasts (unpackaged app) require a registered AppUserModelID + Start Menu shortcut (installer creates the shortcut; app registers AUMID on first run). If a toast fails, fall back to tray balloons for the rest of the session.

## Documentation

- **README.md** (user-facing) has a *Quick start* walkthrough, then a *Reference* section by area, with screenshots from `assets/user-guide/ds-*.png`. It quotes menu items, button labels, dialog text and toasts. When you change one of those, update the README text, and tell the developer which screenshot needs retaking.
- **`.docs/intentional-but-non-obvious.md`** (committed) holds intentional behaviors too in-the-weeds for the README (rollup order, delete vs uninstall cleanup, hotkeys owned by other apps, redaction).
- **`.docs/microsoft-store-distribution-roadmap.md`** (committed) is the deferred MSIX/Store plan. Keep its "what changes" table current when platform seams change.
- **`.docs/DESIGN.md`** and **`.docs/TODO.md`** are gitignored scratch. DESIGN.md's *Running release notes* become the GitHub release body in `/build-release`, so add user-visible changes there as you go.
