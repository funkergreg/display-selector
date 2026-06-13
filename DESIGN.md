# Display Selector — Design Document

> Status: **DRAFT v0.1** — iterating interactively. Implementation has **not** been approved yet.
> Target environment: Windows 11 Pro (developer's machine only; other Windows versions untested but code kept portable).

---

## 1. Overview

**Display Selector** is a lightweight, mostly-idle Windows 11 system-tray utility that lets the user capture the current display + audio configuration as a named **Profile**, bind each profile to a global hotkey, and switch the whole machine to a profile with a single keypress — even from the desktop, without opening any UI.

### Primary use cases (from the developer's setup)
- **Work** — multi-monitor, attached PC speakers.
- **Couch gaming** — single TV display, soundbar via the TV's optical out.
- **Desk gaming** — single monitor, attached PC speakers.

### Core problems being solved
1. Switching profiles today requires several manual steps (main display, sound device, powering monitors).
2. The Windows display settings UI sometimes anchors to an off/remote monitor that's hard to reach.
3. Picking the sound device is tedious and the built-in volume UI is sluggish.
4. **System Sounds** is sometimes left on the old device (treated as a separate app in Volume Mixer).
5. Idle monitors left "connected but off" cause the cursor/windows to wander onto dead displays.

---

## 2. Decisions locked in (from planning Q&A)

| Area | Decision | Rationale |
|---|---|---|
| **UI framework** | **WinForms** (.NET) | Smallest footprint for an idle tray app; trivial `NotifyIcon`; simplest P/Invoke for the Win32 APIs we depend on. |
| **Packaging** | **Inno Setup** installer (unpackaged app) | Appears in Settings ▸ Apps; uninstaller explicitly purges AppData; no code-signing friction for GitHub releases; full compatibility with the undocumented audio API + global hotkeys. |
| **Hotkeys** | **Configurable mapping**, default **F9–F12** | Rebindable (key + modifiers), conflict detection on registration; architected so the default set works out of the box. |
| **Display scope** | **Topology + primary + resolution + orientation** | Captured in one CCD config blob. Per-display DPI/scale deferred (stretch). |
| **Auto-start** | **Yes, with a toggle** | Per-user `Run` key so hotkeys work after boot; checkbox in tray menu; removed on uninstall. |
| **Notifications** | **Windows toast + tray icon/tooltip update** | Native Win11 toast + active-profile reflected in tray. Confirmation sound always played on the newly-selected device. |
| **Audio scope** | **Default output device only** (all 3 roles) | Moves every app + System Sounds; does not touch volume levels. |
| **Debug log** | **Rolling log file in LocalAppData** | Survives sessions, easy to attach to issues, removed on uninstall. |

---

## 3. Runtime / build choices

- **Runtime:** .NET 10 (LTS), `net10.0-windows`, WinForms enabled.
- **Publish:** self-contained, single-file, `win-x64`, trimmed where safe — so end users need no .NET install.
- **Single instance:** named `Mutex`; a second launch surfaces the existing instance's tray menu and exits.
- **Resource model:** purely event-driven. No polling loops. When idle the process just holds a tray icon + a hidden message-only window for hotkey messages. CPU ≈ 0 when not switching.

---

## 4. Architecture

The app is layered so platform-specific code sits behind interfaces. This satisfies the "modular/abstracted enough to update later" goal — e.g. a future `LegacyDisplayService` (pre-CCD) or a non-Windows port would implement the same interfaces.

```
Program.cs ─ single-instance guard, bootstrap
   │
   ▼
TrayApplicationContext ─ owns NotifyIcon, menu, wiring (the "controller")
   │
   ├── IProfileStore        ← profile persistence (JSON)
   ├── IDisplayService       ← capture/apply display config   (CCD API)
   ├── IAudioService         ← capture/apply default device   (Core Audio + IPolicyConfig)
   ├── IHotkeyService        ← register/handle global hotkeys  (RegisterHotKey)
   ├── INotificationService  ← toasts + tray balloon + sound
   ├── IAutoStartManager     ← login auto-start (Run key)
   └── ILog                  ← rolling file logger
```

A **Profile activation** is an orchestrated sequence in the controller:

1. Log "activating <name>" + the target config.
2. `IDisplayService.Apply(profile.Display)` — set topology/primary/resolution/orientation. (Disconnecting a display here drops that monitor into standby/low-power automatically.)
3. `IAudioService.SetDefaultDevice(profile.Audio.DeviceId)` for **all three roles**.
4. `INotificationService.PlayConfirmation(deviceId)` — short embedded sound rendered to the *now-selected* endpoint.
5. `INotificationService.ShowToast("Switched to: <name>")` + update tray tooltip/active marker.
6. Log result; on any failure, log + toast a clear error and leave the system in the best partial state achievable.

---

## 5. Subsystem design

### 5.1 Display — CCD API (`IDisplayService`)
- **Capture:** `GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS)` → `QueryDisplayConfig` → store the full `DISPLAYCONFIG_PATH_INFO[]` + `DISPLAYCONFIG_MODE_INFO[]` arrays. This blob already encodes extend/duplicate/disconnect, which source is primary (position `{0,0}`), resolution, and orientation.
- **Apply:** re-query current paths, **remap saved paths onto current hardware by a stable identifier** (see risk §7.1), then `SetDisplayConfig(SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_APPLY)` (optionally `| SDC_SAVE_TO_DATABASE`).
- **Stable identity — port-first, hybrid:** adapter `LUID`s are *not* stable across reboots, so we never match on them directly. Each CCD path connects a *source* (GPU view) to a *target* (physical GPU connector). For each target we read, via `DisplayConfigGetDeviceInfo`:
  - `outputTechnology` (HDMI / DisplayPort / DVI / …) + `connectorInstance` (which port of that type) → a **port-based key** that follows the *cable/port*, independent of which monitor (or none) is attached.
  - `monitorDevicePath` / EDID (manufacturer + product + serial) → a **monitor-based key**.

  We persist **both** and a small set of raw IDs as hints. At apply time we match **port-first, EDID-fallback**. Port-first is the better fit for this setup (fixed, never-replugged cabling) and aligns with the "don't wear out the HDMI/DP connectors" goal.
- **Hard limit (documented, not solvable in software):** many displays — TVs over HDMI especially — *deassert hot-plug-detect (HPD) when powered off*, so Windows stops exposing that target as connectable until it's powered back on. This is fine for the intended workflow (you power the target display on as part of switching to that location). The only mitigation for "reachable while physically off" is a hardware **HDMI EDID-emulator passthrough**; noted as optional, out of software scope.
- **Low-power behavior:** a display marked disconnected in the applied topology is dropped by the OS, letting the physical panel enter standby — exactly the desired behavior. No extra DDC/CI work needed for v1.

### 5.2 Audio — Core Audio + IPolicyConfig (`IAudioService`)
- **Enumerate / capture:** `IMMDeviceEnumerator` (via NAudio) to list active render endpoints and read the current default. Persist the **endpoint ID string** (stable across reboots), plus a friendly name for display.
- **Apply:** custom COM interop for the undocumented `IPolicyConfig::SetDefaultEndpoint`, called for **`eConsole`, `eMultimedia`, `eCommunications`** so all apps *and* System Sounds follow. This is the fix for the "System Sounds left behind" pain point.
- **Confirmation sound:** render a short embedded `.wav` via WASAPI **directly to the selected endpoint** (deterministic even if default-change propagation lags). Does **not** alter volume.
- **Abstraction note:** `IPolicyConfig` is undocumented; it lives entirely behind `IAudioService` so a future replacement is a one-file swap.

### 5.3 Hotkeys (`IHotkeyService`)
- `RegisterHotKey` against a hidden message-only window; `WM_HOTKEY` messages dispatched to the controller. Passive, ~zero cost.
- Supports modifiers (`MOD_CONTROL | MOD_ALT | MOD_SHIFT | MOD_WIN`) + a virtual key, so custom combos like Ctrl+Alt+G are possible. Defaults: F9–F12 mapped to the first four profiles.
- **Conflict handling (corrected from earlier draft):** `RegisterHotKey` **cannot override or steal** a combo another application already owns — it simply *fails*. So there is no "confirm to override Windows' mapping" flow; instead:
  - **Detect failure** on registration → dialog: "Hotkey X is already in use by another application and can't be assigned — choose another." We refuse to save an unusable binding.
  - **Proactively warn/block** a curated list of OS-reserved combos (Win+L, Ctrl+Alt+Del, etc.) that can't be captured at all.
  - **Confirm-to-reassign** only for *intra-app* conflicts (a combo already bound to another of our profiles) — a "move it to this profile?" confirmation.
  - In practice, robust port-first display remapping (§5.1) makes external conflicts rare since we don't need to change bindings per arrangement.
- **Scope note:** `RegisterHotKey` is system-global by design. The prompt's idea of "only when desktop is focused" isn't natively supported without a low-level hook (heavier, anti-cheat-sensitive). Recommendation: keep system-global registration (low risk, low cost) and rely on uncommon defaults/modifiers; revisit a focus filter only if a real conflict appears.

### 5.4 Notifications (`INotificationService`)
- **Toast:** Win11 toast via CommunityToolkit notifications. Unpackaged apps need a registered **AppUserModelID + Start Menu shortcut** for toasts to display — the Inno installer creates the shortcut and the app registers the AUMID on first run.
- **Tray:** `NotifyIcon` tooltip shows the active profile; menu shows a check/marker next to the active profile (best-effort: a profile is "active" if current live config matches it).
- **Sound:** see §5.2.

### 5.5 Storage (`IProfileStore`)
- **Location:** `%LOCALAPPDATA%\DisplaySelector\` (non-roaming — profiles are hardware-specific to this machine).
- **Files:**
  - `config.json` — app settings (auto-start flag, default-profile, log level).
  - `profiles.json` — array of profiles.
  - `logs\displayselector-*.log` — rolling logs.
  - `assets` (sound/icon) ship inside the exe; nothing user-specific outside this folder.
- **Format (JSON, human-readable):**
```jsonc
{
  "schemaVersion": 1,
  "profiles": [
    {
      "id": "guid",
      "name": "Couch Gaming",
      "hotkey": { "modifiers": ["None"], "key": "F10" },
      "display": { "pathInfo": "<base64 blob>", "modeInfo": "<base64 blob>",
                   "targets": [ { "stableId": "...", "friendly": "LG TV", "primary": true } ] },
      "audio": { "endpointId": "{0.0.0.00000000}.{guid}", "friendlyName": "Soundbar (Optical)" },
      "createdUtc": "2026-06-13T00:00:00Z"
    }
  ]
}
```
- Versioned schema (`schemaVersion`) so future migrations are clean.
- **Durable writes:** atomic save (write `*.tmp` → `File.Replace` over the target) with a retained `profiles.json.bak`, so a crash mid-write can't corrupt the store; a corrupt/missing file is logged and recovered from `.bak` (or starts empty) rather than throwing.
- **Save/load are logged** (see §5.6) so the data shape is inspectable during integration/active tests.

### 5.6 Logging (`ILog`)
- Minimal custom rolling file logger (no external dependency to keep the footprint small). Size-capped (e.g. 5×1 MB files).
- **Levels:** `Info` (default) and `Debug` (toggled via Diagnostics ▸ Enable debug logging, persisted in `config.json`).
- **Always logged (Info):** the full resolved settings on **save** and on **activation** (the prompt's requirement); profile **load** at startup (schema version, profile count, per-profile summary, any `.bak` recovery/migration); every store write (path, byte count, atomic-replace result); activation results and all errors.
- **Debug-level additions (for integration/active tests):** the full serialized profile JSON, raw display config blob sizes + decoded target list (port key, EDID, resolution, primary), audio endpoint IDs, and a trace of each platform API call + return code. This is what makes the **data shape** debuggable on a test machine.
- No UI listing of logs; **Open log folder** + **Copy diagnostics** (§6.3) cover retrieval.

### 5.7 Auto-start (`IAutoStartManager`)
- Per-user `HKCU\...\Run` entry pointing at the installed exe with a `--tray` arg. Toggled by the tray checkbox; written/removed accordingly. Uninstaller removes it.

---

## 6. UI surface (WinForms)

### 6.1 Tray menu (single source of all commands)
Every capability is reachable here; **hotkeys are an accelerator only, never the sole path.**
- **Active-profile header** — shows the active profile name, or *"Custom (unsaved)"* when live settings match no saved profile. The matching profile in the list is check-marked.
- Profiles list — click to activate; each row shows its hotkey.
- **Save current settings as new profile…**
- **Manage profiles** ▸ Rename… / Delete… (confirm) / Set hotkey…
- **Diagnostics** ▸ (see §6.3)
- **Start with Windows** (checkbox)
- **Open log folder** · **About** · **Exit**

### 6.2 Action → reachability & feedback matrix
Principle: every action gives **visual** feedback; the **confirmation tone is reserved for audio-device changes** (we don't beep on menu clicks).

| Action | Menu | Hotkey | Visual feedback | Tone | Logged |
|---|---|---|---|---|---|
| Save new profile | ✓ | — | name dialog → toast "Saved 'X'" + new menu row | — | ✓ full config |
| Activate profile | ✓ | ✓ | toast "Switched to X" + tray tooltip + active-mark | ✓ on new device | ✓ target + result |
| Activate (partial/fail) | ✓ | ✓ | **error toast** listing unavailable targets | ✓ if audio applied | ✓ errors |
| Activate (already active) | ✓ | ✓ | normal toast — **re-applies** (the "unstick" fix) | ✓ | ✓ |
| Rename | ✓ | — | dialog → toast "Renamed to X" + menu refresh | — | ✓ |
| Delete | ✓ | — | confirm dialog → toast "Deleted X" + row removed | — | ✓ |
| Set/change hotkey | ✓ | — | capture dialog w/ live conflict check; success toast / inline error (§5.3) | — | ✓ |
| Toggle auto-start | ✓ | — | checkmark + balloon "Will / won't start with Windows" | — | ✓ |
| Toggle debug logging | ✓ | — | checkmark + balloon | — | ✓ |
| Run audio test | ✓ | — | per-device play + Yes/No dialog → results summary | ✓ per device | ✓ results |
| Run display test | ✓ | — | dialog: decoded targets + `SDC_VALIDATE` result | — | ✓ |
| Copy diagnostics | ✓ | — | balloon "Copied to clipboard" | — | — |
| Open log folder | ✓ | — | opens Explorer | — | — |
| Second launch (already running) | — | — | balloon "Already running" + surfaces menu | — | ✓ |
| Exit | ✓ | — | tray icon removed | — | ✓ shutdown |

No command is hotkey-only, and no state change is silent.

### 6.3 Diagnostics submenu (runtime self-test — the human-in-the-loop tier)
A runtime feature (distinct from dev-time xUnit tests in §8) so the developer **or any user of the open-source build** can verify behavior on their own hardware:
- **Enable debug logging** (checkbox, persisted) — raises log level to Debug: logs full data shapes, raw config blobs, and API call traces.
- **Run audio test…** — lists detected output endpoints; plays the confirmation tone routed to each (or a selected) endpoint, then asks *"Did you hear it on `<device>`? Yes / No."* Verifies **find + route + actual playback** with human confirmation of the physical outcome.
- **Run display test…** — non-destructive: shows the decoded targets the tool sees (port key, EDID, resolution, which is primary) for the human to confirm correctness, and runs `SetDisplayConfig(SDC_VALIDATE)` on a chosen profile *without applying*. (A real switch test = simply activating a profile.)
- **Copy diagnostics** — copies app version, OS build, GPU, and the detected display+audio inventory to the clipboard for GitHub issues.
- **Open log folder.**

### 6.4 Dialogs
Save (name entry), Rename, Delete confirmation, Hotkey capture (records key+modifiers, live conflict check), Audio test, Display test. All small, utilitarian, keyboard-friendly.

---

## 7. Key risks & open questions

1. **Display target matching across power cycles / reboots** *(highest risk)* — LUIDs change; we mitigate with a **port-first, EDID-fallback** stable key and remap-on-apply (§5.1). Needs real hardware testing with monitors powered off at apply time. **Resolved:** when a profile references a monitor that is physically disconnected (not just off), apply best-effort and log + toast which targets were unavailable. **Known hardware limit:** displays that drop HPD when powered off won't be reachable until powered on (TVs over HDMI); optional HDMI EDID-emulator passthrough is the only workaround, out of software scope.
2. **`IPolicyConfig` fragility** — undocumented; isolated behind `IAudioService`. Acceptable for personal use; documented as a known risk in README.
3. **Toast prerequisites** — AUMID + Start Menu shortcut required for unpackaged toasts; handled by installer + first-run registration. Fallback to tray balloon if toast registration fails.
4. **"Remember window locations based on monitor connection"** — OS-managed; makes Windows restore window positions per monitor arrangement on reconnect (likely *helpful* with profiles; when a monitor disconnects its windows pile onto remaining displays — standard Windows behavior). **Decision:** do not serialize; leave to the OS for v1 and verify during testing it doesn't fight topology changes.
5. **Duplicate mode with mismatched native resolutions** — edge cases; rely on CCD validation (`SDC_VALIDATE`) before apply and report failures.
6. **Volume not saved** — by decision; note in README so it's not perceived as a bug.

---

## 8. Build, test & release cycle

- **IDE:** VS Code + C# Dev Kit; build via `dotnet` CLI (no Visual Studio required).
- **Inner loop:** `dotnet build` → `dotnet test` (unit only by default) → `dotnet run` to launch the tray app.
- **Installer:** Inno Setup (`iscc.exe`); `build/build.ps1` runs `dotnet test` → `dotnet publish` → compiles `installer/setup.iss`. Flags: `-IncludeIntegration` to also run tier-2 tests; `-SkipTests` for a fast package.
- **VS Code tasks:** `tasks.json` for *build*, *run*, *test (unit)*, *test (integration)*, *publish*, *package (installer)*.

```
display-selector/
├─ DESIGN.md                ← this file
├─ CLAUDE.md                ← agentic-coding guide (created when design is approved)
├─ README.md  LICENSE  .gitignore
├─ DisplaySelector.sln
├─ src/DisplaySelector/
│  ├─ DisplaySelector.csproj
│  ├─ Program.cs
│  ├─ App/TrayApplicationContext.cs
│  ├─ Core/
│  │  ├─ Profiles/ (Profile.cs, IProfileStore.cs, JsonProfileStore.cs)
│  │  ├─ Display/ (IDisplayService.cs, CcdDisplayService.cs, Interop/…)
│  │  ├─ Audio/   (IAudioService.cs, CoreAudioService.cs, Interop/IPolicyConfig.cs)
│  │  ├─ Hotkeys/ (IHotkeyService.cs, HotkeyService.cs, HotkeyWindow.cs)
│  │  ├─ Notifications/ (INotificationService.cs, ToastNotificationService.cs)
│  │  ├─ Startup/ (IAutoStartManager.cs, RunKeyAutoStart.cs)
│  │  └─ Logging/ (ILog.cs, FileLogger.cs)
│  ├─ UI/ (SaveProfileDialog.cs, RenameDialog.cs, HotkeyDialog.cs, …)
│  └─ assets/ (icon.ico, confirm.wav)
├─ tests/DisplaySelector.Tests/   ← xUnit; unit + integration via [Trait("Category",…)]
├─ installer/setup.iss
└─ build/build.ps1
```

- **Dependencies (intentionally few):** NAudio (audio enumeration + WASAPI test playback), CommunityToolkit notifications (toasts). Everything else is custom P/Invoke. Logger is hand-rolled to avoid a dependency.

### 8.1 Testing strategy (three tiers)
The layered interfaces (§4) are what make tiers 1–2 possible — platform code is mockable.

**Tier 1 — Unit (xUnit, headless, fast, CI-safe).** No hardware; mock `IDisplayService`/`IAudioService`/etc.
- Profile/config JSON **round-trip** — the data-shape contract (catches accidental schema breaks).
- `JsonProfileStore`: save/load/rename/delete, atomic write + `.bak` recovery, corrupt-file handling, schema migration.
- Hotkey parsing/formatting (key+modifiers ↔ string), conflict logic, reserved-combo blocklist.
- Display target-key derivation from a **captured fixture blob** (port-first/EDID-fallback matching).
- **Activation orchestration** with mocked services: asserts the exact step order, that partial failures are surfaced + logged, and that idempotent re-apply works.
- Logger rotation/size-cap.

**Tier 2 — Integration (real Windows APIs, non-destructive, needs a desktop session).** Marked `[Trait("Category","Integration")]`, excluded from the default run.
- Enumerate render endpoints → assert ≥1 found and current default is readable.
- `QueryDisplayConfig` → assert paths returned and the blob round-trips through our decode.
- These verify the app can **find** devices/displays without changing anything.

**Tier 3 — Active / human-in-the-loop (the physical outcome).** A machine can't assert "the tone played on the soundbar," so these run from the in-app **Diagnostics** menu (§6.3): play tone per endpoint → human confirms Yes/No; validate a profile (`SDC_VALIDATE`) or do a real switch → human confirms displays/sound changed. Results are logged. This tier doubles as the verification path for the **open-source release on unknown hardware**.

**Run commands:** `dotnet test` (unit) · `dotnet test --filter Category=Integration` · Diagnostics menu (tier 3).

---

## 9. CLAUDE.md plan (for agentic implementation)

To be created on approval. It will document, concisely:
- Build/run/package commands (so agents don't rediscover them and burn context).
- The layered architecture + the rule "platform code lives behind an interface."
- Coding conventions (nullable enabled, file-scoped namespaces, async only where it earns its keep).
- The two "here be dragons" areas (CCD target matching, IPolicyConfig) and how they're isolated.
- "Don't commit/push — the developer does that for QC."

### Recommended Claude Code skills
- **A project `build-release` skill** — wraps `dotnet test` + `dotnet publish` + Inno compile into one step (repetitive; good to offload from context). Created under `.claude/skills/`.
- **A `run-tests` skill** — runs `dotnet test` (unit) and, on request, the `Category=Integration` filter, summarizing failures. Keeps the test loop out of the main context.
- Use the existing **`/code-review`** and **`/security-review`** on diffs before the developer commits.
- Use **`/run`** / **`/verify`** to launch and sanity-check the tray app after changes (note: tier-3 physical checks still need the human via the Diagnostics menu).
- Optionally a small **`add-pinvoke`** skill/reference capturing our P/Invoke conventions, since interop signatures are repetitive and error-prone.

---

## 10. Proposed milestones (post-approval)

1. **M0 – Scaffold:** ✅ repo, csproj, tray shell that runs idle, logger, JSON store (atomic write + `.bak`), **xUnit test project**, CLAUDE.md, build script + installer skeleton.
2. **M1 – Audio:** ✅ enumerate/capture/apply default device (all roles) + confirmation sound. Unit + integration tests; **Diagnostics ▸ Run audio test**.
3. **M2 – Display:** ✅ CCD capture/apply with port-first/EDID remapping; topology/primary/resolution/orientation. Struct-size + blob unit tests; **Diagnostics ▸ Run display test**. (Cross-reboot remap exercised in M3 activation.)
4. **M3 – Profiles + hotkeys:** ✅ save/rename/delete, hotkey mapping + conflict detection, activation orchestration (full feedback matrix §6.2). Validated end-to-end on real hardware incl. both unstick cases (re-apply current + powered-off TV).
5. **M3.5 – Audio enhancements** *(added from M3 feedback)*:
   - **Per-profile audio editing** — Manage profiles ▸ *Set audio device…* to change a profile's default-output endpoint without recapturing the whole profile.
   - **Audio-only profiles** — a profile with **no display change** (`Display == null`) switches only the default device; `ProfileActivator` already supports this, so an **audio-only hotkey toggle** falls out for free. Add a *Save current audio device as profile* path and/or let a profile be audio-only.
   - **Assign-to-profile from the audio test dialog** — set the selected device into a chosen profile from *Run audio test…*.
6. **M4 – Notifications + auto-start + tray UX polish** ✅ real Win11 toasts (Microsoft.Toolkit.Uwp.Notifications) with **fixed tag/group so each toast replaces the previous** (fixes the queue lag) + tray-balloon fallback; **tray icon** from `assets/` (embedded multi-size `app.ico`); **HKCU Run auto-start** wired to *Start with Windows*. Background-thread activation still **deferred** (activation responsiveness was fine).
7. **M5 – Installer + uninstall cleanup (AppData purge) + README (banner) + release binaries.**
8. **Stretch / deferred:**
   - **Confirmation-tone timing for waking-from-standby devices** *(from M3 feedback, low priority)* — the tone can fire before a TV/optical link is awake and be lost; consider a delayed/retried tone. Other paths verify sound, so deferred.
   - Per-display scale/DPI; resolution/orientation editing; pre-CCD fallback; CI workflow for tier-1 tests.

---

## 11. Changelog
- **v0.4** — M0–M3 delivered + validated on hardware. Menu hotkey label changed to `Name : Hotkey`. Added **M3.5 audio enhancements** (per-profile audio editing, audio-only profiles + audio-only hotkey toggle, assign-device-to-profile from audio test) and deferred **confirmation-tone-timing** enhancement — all from M3 feedback.
- **v0.3** — UI completeness audit: full **action→reachability→feedback matrix** (§6.2), every command reachable from the tray menu, no silent state changes, confirmation tone reserved for audio changes, active-profile/"Custom (unsaved)" indicator, idempotent re-apply as the "unstick" fix. Added **Diagnostics submenu** (§6.3: debug-logging toggle, human-in-the-loop audio/display tests, copy diagnostics). Durable atomic store writes + `.bak`. Expanded load/save + debug-level data-shape logging (§5.6). New **three-tier testing strategy** (§8.1) and build/test cycle; added `run-tests` skill and test milestones.
- **v0.2** — Display identity changed to **port-first, EDID-fallback** hybrid (follows fixed cabling, reduces connector wear); documented HPD/EDID-drop hardware limit. Corrected hotkey conflict model (`RegisterHotKey` can't override others — detect/fail + reassign-within-app + reserved-combo blocklist). Resolved (3) best-effort apply and (4) leave window-location setting to OS.
- **v0.1** — Initial draft from planning Q&A (framework, packaging, hotkeys, display scope, auto-start, notifications, audio scope, logging all decided).
```
