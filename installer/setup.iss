; Inno Setup script for Display Selector.
; Per-user install (no admin): app under per-user Program Files, HKCU Run key removed on uninstall,
; and all generated data under %LOCALAPPDATA%\DisplaySelector purged.
;
; SILENT INSTALL (required for a Microsoft Store EXE submission — see docs/microsoft-store-distribution-roadmap.md):
;   DisplaySelectorSetup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
; SILENT UNINSTALL:
;   "%LOCALAPPDATA%\Programs\Display Selector\unins000.exe" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
; (A trusted-CA code-signing certificate is also required for Store EXE submission; GitHub releases
;  work unsigned, with a SmartScreen prompt.)

#define AppName "Display Selector"
; The version is single-sourced from the app .csproj <Version> and passed by build.ps1 via
; /DAppVersion=. This #define is only the fallback for a direct `iscc setup.iss` compile; keep it
; in sync with the .csproj if you compile the installer without the build script.
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppPublisher "FunkerGreg"
#define AppExe "DisplaySelector.exe"
; Keep AppUserModelId in sync with AppIdentity.AppUserModelId in the app.
#define AppUserModelId "FunkerGreg.DisplaySelector"

[Setup]
AppId={{7C9B2E14-3F6A-4D8E-9C1B-2A5E6F0D4B73}}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=DisplaySelectorSetup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#AppName}
; Branding: Setup.exe icon, the wizard's small (top) image, and the Settings ▸ Apps icon.
SetupIconFile=..\src\DisplaySelector\assets\app.ico
WizardSmallImageFile=wizard-small.bmp
UninstallDisplayIcon={app}\{#AppExe}
; Show the Apache 2.0 license for acceptance during install.
LicenseFile=..\LICENSE
; Inno can use our single-instance mutex to detect a running instance during install/uninstall.
AppMutex=Local\DisplaySelector.SingleInstance

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; Expects build.ps1 to have published to ..\publish (self-contained single-file). Exclude debug symbols.
Source: "..\publish\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\NOTICE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Start Menu shortcut carries the AppUserModelID so unpackaged toast notifications work.
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"; AppUserModelID: "{#AppUserModelId}"

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[Registry]
; Declared with dontcreatekey + uninsdeletevalue: the app writes this value at runtime when the
; user enables auto-start; this entry ensures the uninstaller removes it cleanly.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: none; ValueName: "DisplaySelector"; \
    Flags: dontcreatekey uninsdeletevalue

[UninstallDelete]
; Purge all user-generated data (profiles, config, logs) on uninstall.
Type: filesandordirs; Name: "{localappdata}\DisplaySelector"

[Code]
{ Force-close a running instance before install/uninstall so file + AppData cleanup completes
  (the tray app would otherwise keep its folder/exe in use). The AppMutex directive is the backstop. }
procedure StopRunningApp;
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/IM {#AppExe} /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function InitializeSetup(): Boolean;
begin
  StopRunningApp();
  Result := True;
end;

function InitializeUninstall(): Boolean;
begin
  StopRunningApp();
  Result := True;
end;
