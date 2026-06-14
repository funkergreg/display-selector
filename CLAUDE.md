# CLAUDE.md — Display Selector

Guidance for agentic coding on this repo. Keep this file current as the source of truth for *how to work here*; it must stay self-sufficient (DESIGN.md is a disposable working doc and will be deleted — do not depend on it long-term; a README.md will become the user-facing doc near feature-complete).

## What this is
A lightweight, mostly-idle **Windows 11** system-tray utility (personal use, open-sourced). It captures the current **display + default audio device** as a named **Profile**, binds each profile to a **global hotkey**, and switches the whole machine to a profile with one keypress. WinForms + .NET 10, distributed via an Inno Setup installer.

## Golden rules
- **Do NOT `git commit` or `git push`.** The developer does all commits/pushes as a QC step. You may run read-only git (`status`, `diff`, `log`).
- **Windows 11 only** is the test target. Keep code portable (platform behind interfaces) but don't spend effort on other-OS/older-Windows support unless asked.
- **Platform code lives behind an interface.** Anything touching Win32/COM goes behind `IDisplayService` / `IAudioService` / `IHotkeyService` / `INotificationService` / `IAutoStartManager` / `ILog`, so it stays mockable and swappable.
- Prefer few dependencies. Current allowed set: **NAudio** (audio enumeration + WASAPI test playback) and **Microsoft.Toolkit.Uwp.Notifications** (Win11 toasts via the unpackaged compat layer). Logger is hand-rolled. Clear new dependencies with the developer first.

## Commands
> Project is scaffolded in M0; these are the intended commands.
- Build: `dotnet build`
- Run the tray app: `dotnet run --project src/DisplaySelector`
- Unit tests (headless, default loop): `dotnet test --filter "Category!=Integration"`
- Integration tests (real Windows APIs, non-destructive, needs a desktop session): `dotnet test --filter "Category=Integration"`
- Publish + package installer: `powershell -ExecutionPolicy Bypass -File build/build.ps1` (or `pwsh` if installed; flags: `-IncludeIntegration`, `-SkipTests`). Requires Inno Setup 6 for the installer step.
- Tier-3 physical checks (sound actually plays, displays actually switch) are **human-in-the-loop** via the app's in-tray **Diagnostics** menu — not automatable.

## Skills (prefer these over ad-hoc commands)
- **`/build-release`** — runs unit tests → publish → Inno Setup compile; reports the installer path.
- **`/run-tests`** — runs unit tests by default; integration on request; summarizes failures.
- Use **`/code-review`** and **`/security-review`** on the diff before telling the developer it's ready to commit.

## Architecture (layered)
```
Program.cs (single-instance Mutex, bootstrap)
  └─ TrayApplicationContext (controller: owns NotifyIcon + menu, wires services)
       ├─ IProfileStore   → JsonProfileStore  (atomic write + .bak, %LOCALAPPDATA%\DisplaySelector\)
       ├─ IDisplayService → CcdDisplayService (QueryDisplayConfig / SetDisplayConfig)
       ├─ IAudioService   → CoreAudioService  (Core Audio + IPolicyConfig, all 3 roles)
       ├─ IHotkeyService  → HotkeyService     (RegisterHotKey on a message-only window)
       ├─ INotificationService → toasts + tray balloon + confirmation tone
       ├─ IAutoStartManager → HKCU Run key
       └─ ILog → FileLogger (rolling, Info/Debug levels)
```
Profile **activation** is one orchestrated sequence in the controller: log → apply display → set default audio (all roles) → play tone on new device → toast + tray update → log result. Failures apply best-effort and are surfaced (toast) + logged. Re-applying the active profile is intentional (the "unstick a frozen Windows display UI" fix).

## Here be dragons (the two fragile areas — keep isolated, test hard)
1. **Display target matching across reboots/power cycles** (`CcdDisplayService`). Adapter LUIDs are **not** stable across reboots. Match targets **port-first** (`outputTechnology` + `connectorInstance`) with **EDID/monitorDevicePath fallback**; persist both. Hard hardware limit: displays that drop HDMI hot-plug-detect when powered off won't be reachable until powered on — handle best-effort + report, don't fight it.
2. **`IPolicyConfig::SetDefaultEndpoint`** (`CoreAudioService`) is **undocumented** COM. Call it for all three roles (`eConsole`, `eMultimedia`, `eCommunications`) so every app + System Sounds follows. Keep all interop in one file behind `IAudioService` for easy replacement.

## Data & storage
- Location: `%LOCALAPPDATA%\DisplaySelector\` (non-roaming — profiles are hardware-specific). Files: `config.json`, `profiles.json` (+ `.bak`), `logs\`.
- JSON, human-readable, with `schemaVersion` for migrations. Writes are atomic (tmp → `File.Replace`) with a retained `.bak`; corrupt/missing files recover from `.bak` or start empty (logged, never throw).
- Log full resolved settings on **save** and **activation**; at **Debug** level also log full serialized JSON + decoded display targets + audio endpoint IDs + API call traces (this is how data shape is debugged on a test machine).
- **Uninstall must purge everything** under `%LOCALAPPDATA%\DisplaySelector\` and remove the `Run` key — the Inno uninstaller handles this.

## Conventions
- .NET 10 (`net10.0-windows10.0.19041.0` — the Windows-SDK TFM unlocks the WinRT toast projections), WinForms; nullable enabled; file-scoped namespaces; `async` only where it earns its keep (this app is mostly synchronous + event-driven).
- Every capability must be reachable from the tray menu (hotkeys are accelerators only). No silent state changes — every action gives visual feedback; the **confirmation tone is reserved for audio-device changes**.
- Single instance enforced via a named `Mutex`; second launch surfaces the existing menu and exits.
- Toasts (unpackaged app) require a registered AppUserModelID + Start Menu shortcut (installer creates the shortcut; app registers AUMID on first run). Fall back to tray balloon if toast registration fails.
