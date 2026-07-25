; Inno Setup script for MonitorPin.
; Build with: installer\build-installer.ps1  (publishes the app, then compiles this).
; Get Inno Setup from https://jrsoftware.org/isdl.php

#define AppName "MonitorPin"
#define AppVersion "1.0.0"
#define AppExe "MonitorPin.exe"
#define Publisher "AemiliusXIV"
#define RepoUrl "https://github.com/AemiliusXIV/MonitorPin"

[Setup]
AppId={{B7E6B2A1-6C3D-4E9A-9F21-2A1D9C4E7F30}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExe}
OutputDir=output
OutputBaseFilename=MonitorPin-Setup-{#AppVersion}
SetupIconFile=..\MonitorPin.ico
; Version info is stamped onto Setup.exe AND the uninstaller, so the elevation
; prompt shows "MonitorPin Setup"/"MonitorPin Uninstaller" with the app icon
; instead of a bare "unins000.exe".
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
VersionInfoCompany={#Publisher}
VersionInfoDescription={#AppName} Setup
VersionInfoCopyright=Copyright (c) 2026 {#Publisher}
UninstallDisplayName={#AppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Detect a running MonitorPin (the app holds these) so an upgrade can close it
; before replacing the exe, instead of failing on a locked file.
AppMutex=MonitorPin.SingleInstance,Global\MonitorPin.SingleInstance
CloseApplications=yes
RestartApplications=no
; Installing to Program Files needs admin; this is the install prompt only,
; not the app's own runtime elevation (which the app manages via a logon task).
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Files]
; A self-contained single-file publish: one exe, no .NET needed on the target PC.
Source: "..\bin\Release\net8.0-windows\win-x64\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Shortcuts pass --show so launching by hand opens the window; the logon task
; (created separately) launches without it and stays in the tray.
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"; Parameters: "--show"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Parameters: "--show"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "startupwin"; Description: "Start {#AppName} automatically with Windows"; GroupDescription: "Startup:"

[Run]
; Setup already has admin, so register the logon task here. This means the user
; never sees a UAC prompt for it later, and the task points at the installed
; Program Files path rather than wherever the exe happened to be run from.
Filename: "{app}\{#AppExe}"; Parameters: "--install-task"; Tasks: startupwin; Flags: runhidden waituntilterminated
; Start now with the window shown, so the user sees it launched and can add a
; first rule. This session runs elevated (Setup is), matching how the logon task
; will start it after a reboot; the task handles every login from then on.
Filename: "{app}\{#AppExe}"; Parameters: "--show"; Description: "Start {#AppName} now"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Remove the logon task on uninstall so nothing is left behind.
Filename: "{app}\{#AppExe}"; Parameters: "--uninstall-task"; Flags: runhidden waituntilterminated; RunOnceId: "removetask"

[Code]
procedure OpenRepo(Sender: TObject);
var ErrorCode: Integer;
begin
  ShellExec('open', '{#RepoUrl}', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;

procedure InitializeWizard();
var
  Page: TWizardPage;
  Body: TNewStaticText;
  Link: TNewStaticText;
begin
  Page := CreateCustomPage(wpWelcome, 'Please read this first', 'Make sure you have a genuine copy');

  Body := TNewStaticText.Create(Page);
  Body.Parent := Page.Surface;
  Body.Left := 0;
  Body.Top := 0;
  Body.Width := Page.SurfaceWidth;
  Body.AutoSize := False;
  Body.WordWrap := True;
  Body.Height := ScaleY(150);
  Body.Caption :=
    'MonitorPin is free and open source. It will never cost money.' + #13#10 + #13#10 +
    'If you paid for MonitorPin, or downloaded it from anywhere other than the official page below, ' +
    'you do not have a genuine copy. A build from another source could contain malware or quietly ' +
    'track your data. Please stop and get it from the official page instead.' + #13#10 + #13#10 +
    'The only official download is:';

  Link := TNewStaticText.Create(Page);
  Link.Parent := Page.Surface;
  Link.Left := 0;
  Link.Top := Body.Top + Body.Height + ScaleY(2);
  Link.AutoSize := True;
  Link.Caption := '{#RepoUrl}';
  Link.Cursor := crHand;
  Link.Font.Color := clHighlight;
  Link.Font.Style := [fsUnderline];
  Link.OnClick := @OpenRepo;
end;
