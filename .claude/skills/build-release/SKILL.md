---
name: build-release
description: Build and package a release installer for Display Selector. Use when asked to build a release, produce/compile the installer, package the app, or make release binaries. Runs unit tests, publishes a self-contained single-file exe, and compiles the Inno Setup installer.
---

# build-release

Produce a release build + installer for Display Selector. Prefer the `build/build.ps1` script (it encapsulates the canonical pipeline); fall back to manual steps only if the script is missing.

## Steps
1. **Confirm prerequisites** (only if a step later fails — don't pre-check noisily):
   - .NET 10 SDK: `dotnet --version`
   - Inno Setup compiler `iscc.exe` on PATH (or at the usual `C:\Program Files (x86)\Inno Setup 6\ISCC.exe`).
2. **Run the pipeline:**
   - Preferred: `powershell -ExecutionPolicy Bypass -File build/build.ps1` (or `pwsh build/build.ps1` if PowerShell 7 is installed)
   - The script runs, in order: unit tests (`Category!=Integration`) → `dotnet publish -c Release -r win-x64 --self-contained` (single-file) → `iscc installer/setup.iss`.
   - Useful flags: `-SkipTests` (fast package), `-IncludeIntegration` (also run tier-2 tests — requires a desktop session).
3. **If `build/build.ps1` does not exist yet** (early in M0), do it manually:
   - `dotnet test --filter "Category!=Integration"`
   - `dotnet publish src/DisplaySelector -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`
   - `& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer/setup.iss`
4. **Report** the final installer path (e.g. `installer/Output/DisplaySelectorSetup.exe`) and a one-line summary (tests passed? publish size? installer produced?). If any step failed, surface the actual error output — do not claim success.

## Notes
- Never `git commit`/`push` — the developer handles that.
- This skill does **not** cover tier-3 physical verification (sound/display actually changing) — that is human-in-the-loop via the app's Diagnostics menu.
