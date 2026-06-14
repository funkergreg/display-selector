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

5. **Publish the GitHub release** — this is the one place where pushing is allowed (see CLAUDE.md golden rules). Only do this when the developer asks to publish/release; do not auto-publish on every build. Steps:
   - **Confirm first.** A release is public and hard to fully reverse — get the developer's explicit go-ahead before running the command, and surface the exact tag + asset you're about to publish.
   - **Pre-checks:** working tree is clean (`git status`) and the local branch is pushed/up to date with the remote — the release tag must point at a commit that exists on the remote. The developer commits + pushes the source; the release tag is the only thing this skill pushes.
   - **Version:** derive from the app `.csproj` `<Version>` (the single source of truth — same value `build.ps1` stamps into the installer). Tag is `v{version}` (e.g. `v1.0.0`).
   - **Create the release** with the GitHub CLI, attaching the freshly-built installer:
     ```pwsh
     $v = ([xml](Get-Content src/DisplaySelector/DisplaySelector.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
     gh release create "v$v" "installer/Output/DisplaySelectorSetup.exe" --title "Display Selector v$v" --notes "..."
     ```
   - **Don't clobber:** if a release/tag for that version already exists, stop and report — do not overwrite or force. Bump the version (`.csproj`) and rebuild instead.
   - **Report** the release URL `gh` prints.

## Notes
- Pushing is allowed **only** for the step-5 release publish above (tag + `gh release`). Never `git commit`, and never push source branches — the developer handles those as a QC step.
- This skill does **not** cover tier-3 physical verification (sound/display actually changing) — that is human-in-the-loop via the app's Diagnostics menu.
