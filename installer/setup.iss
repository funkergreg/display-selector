; Inno Setup script for Display Selector.
; Per-user install (no admin): app under LocalAppData-friendly Program Files-per-user,
; HKCU Run key removed on uninstall, and all generated data under %LOCALAPPDATA%\DisplaySelector purged.

#define AppName "Display Selector"
#define AppVersion "0.1.0"
#define AppPublisher "FunkerGreg"
#define AppExe "DisplaySelector.exe"
#define AppUserModelId "FunkerGreg.DisplaySelector"

[Setup]
AppId={{B8E2B0A6-1E5E-4D2B-9C6A-DS0PLACEHOLDER01}}
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

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; Expects build.ps1 to have published to ..\publish (self-contained single-file).
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

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
