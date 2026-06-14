# Microsoft Store distribution — roadmap

Status: **investigated, light prep done, full packaging deferred.** Primary distribution today is GitHub Releases (Inno Setup EXE). This doc captures what it takes to also ship via the Microsoft Store so the decision can be made later without re-research.

## Two routes

### Route 1 — Submit the EXE/MSI installer (reuse our Inno setup)
- The Store accepts unpackaged Win32 apps via an installer.
- **Requirements** ([MS Learn: MSI/EXE app package requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msi/app-package-requirements)):
  - **Silent install** (no install UI; a UAC prompt is allowed). Our installer supports `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART` — documented in `installer/setup.iss`.
  - **Code signing with a certificate from a trusted CA** (not self-signed). This is a real cost/identity barrier.
  - Offline-installable; declare package URL, architecture, and the silent switches in Partner Center.
  - Microsoft does **not** host or auto-update it — you host updates yourself.

### Route 2 — MSIX submission
- More setup, better Store integration.
- **Microsoft signs it for you** (no paid cert needed) and provides auto-update + guaranteed clean install/uninstall.
- Needs an `AppxManifest`, an MSIX build step, and the app changes below.
- The undocumented audio API (`IPolicyConfig`) works in a **full-trust** MSIX (`runFullTrust`, not AppContainer). Precedent: EarTrumpet ships an audio-device switcher in the Store using this class of API — so it isn't a certification blocker in practice (verify before submitting).

Both routes require a **Partner Center account** (~$19 one-time individual fee).

## What changes in the app for MSIX (small, thanks to the interfaces)

| Concern | Unpackaged (today) | MSIX |
|---|---|---|
| Auto-start | HKCU `Run` key (`RunKeyAutoStart`) | `windows.startupTask` manifest extension + `Windows.ApplicationModel.StartupTask` — the Run key is virtualized/ignored under MSIX |
| Identity / AUMID | `AppIdentity.AppUserModelId` + installer shortcut | Package manifest Application Id (keep it equal to `AppIdentity.AppUserModelId`) |
| Toasts | Community Toolkit compat shim | AUMID from the manifest (compat shim optional) |
| AppData + uninstall | installer `[UninstallDelete]` purge | automatic container cleanup |
| CCD display / RegisterHotKey / IPolicyConfig | work | work (full-trust package) |

### Seams already in place (light prep, M5)
- **`PackageContext.IsPackaged`** — runtime MSIX detection.
- **`RunKeyAutoStart`** no-ops + logs when packaged, so a future MSIX build won't write an ineffective Run key. Add an `MsixStartupTaskAutoStart : IAutoStartManager` and select it when `PackageContext.IsPackaged`.
- **`AppIdentity`** — single source for name + AUMID, reused by installer and (future) manifest.
- Installer documents the silent switches and does a clean per-user uninstall.

## Deferred work (when pursuing the Store)
1. Pick MSIX tooling: VS "Windows Application Packaging Project", or CLI `MakeAppx` + a hand-authored `AppxManifest.xml` + `SignTool` (keeps the VS-Code/CLI workflow but is more manual). See [Package a .NET app with MSIX](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/dotnet/package-app).
2. Author `AppxManifest.xml`: identity (from Partner Center), `runFullTrust`, the `windows.startupTask` extension, toast/AUMID.
3. Implement `MsixStartupTaskAutoStart` ([StartupTask API](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.startuptask)).
4. Register a Partner Center account + reserve the app name (sets the final Publisher CN / package identity).
5. Validate with the Windows App Certification Kit; confirm the undocumented audio API passes review.

## References
- [MSI/EXE Store requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msi/app-package-requirements)
- [Package a .NET app with MSIX](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/dotnet/package-app)
- [StartupTask class](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.startuptask)
- [MSIX full-trust container](https://learn.microsoft.com/en-us/windows/msix/msix-container)
