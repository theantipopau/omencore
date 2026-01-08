#define MyAppName "OmenCore"
#ifndef MyAppVersion
  #define MyAppVersion "2.2.1"
#endif
#define MyAppPublisher "OmenCore Project"
#define MyAppExeName "OmenCore.exe"
#define PawnIOInstallerUrl "https://pawnio.eu/PawnIO.exe"

[Setup]
AppId={{6F5B6F3F-8FAF-4FC8-A5E0-4E2C0E8F2E2B}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL="https://github.com/theantipopau/OmenCore"
AppSupportURL="https://github.com/theantipopau/OmenCore/issues"
AppUpdatesURL="https://github.com/theantipopau/OmenCore/releases"
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
SetupIconFile=..\src\OmenCoreApp\Assets\OmenCore.ico
; Branding images
WizardImageFile=wizard-large.bmp
WizardSmallImageFile=wizard-small.bmp
Compression=lzma2/ultra64
SolidCompression=yes
OutputDir=..\\artifacts
OutputBaseFilename=OmenCoreSetup-{#MyAppVersion}
PrivilegesRequired=admin
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
; Modern look
WizardResizable=no
DisableWelcomePage=no
LicenseFile=
InfoBeforeFile=
InfoAfterFile=

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startmenu"; Description: "Create Start Menu shortcut"; GroupDescription: "{cm:AdditionalIcons}"
Name: "installpawnio"; Description: "Install PawnIO driver (Secure Boot compatible, recommended for advanced features)"; GroupDescription: "Hardware Drivers:"; Flags: unchecked
Name: "autostart"; Description: "Start OmenCore with Windows"; GroupDescription: "Startup Options:"; Flags: unchecked

[Files]
; Self-contained app with embedded .NET runtime - no separate .NET installation needed
Source: "..\\publish\\win-x64\\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; PawnIO installer (optional)
Source: "PawnIO_setup.exe"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall; Tasks: installpawnio; Check: PawnIOInstallerExists
; Default config
Source: "..\\config\\default_config.json"; DestDir: "{app}\\config"; Flags: ignoreversion onlyifdoesntexist

[Dirs]
Name: "{app}\\logs"; Permissions: users-modify
Name: "{app}\\config"; Permissions: users-modify

[Icons]
Name: "{autoprograms}\\{#MyAppName}"; Filename: "{app}\\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\\{#MyAppName}"; Filename: "{app}\\{#MyAppExeName}"; Tasks: desktopicon; WorkingDir: "{app}"
; NOTE: Startup is now handled via Task Scheduler in the app itself (Settings > Start with Windows)
; This avoids double-startup issues and ensures elevated privileges for hardware access

[Run]
; Install PawnIO driver if bundled
Filename: "{tmp}\\PawnIO_setup.exe"; Parameters: "/SILENT"; StatusMsg: "Installing PawnIO driver (Secure Boot compatible)..."; Flags: waituntilterminated; Tasks: installpawnio; Check: PawnIOInstallerExists
; Create scheduled task for autostart if user selected it (runs with elevated privileges)
Filename: "schtasks"; Parameters: "/create /tn ""OmenCore"" /tr ""\""{app}\\{#MyAppExeName}\"" --minimized"" /sc onlogon /rl highest /f"; Flags: runhidden; Tasks: autostart
; Launch OmenCore with elevation (shellexec verb=runas)
Filename: "{app}\\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent shellexec runascurrentuser; Verb: runas

[UninstallRun]
; Stop OmenCore and HardwareWorker if running
Filename: "taskkill"; Parameters: "/F /IM OmenCore.exe"; Flags: runhidden; RunOnceId: "StopOmenCore"
Filename: "taskkill"; Parameters: "/F /IM OmenCore.HardwareWorker.exe"; Flags: runhidden; RunOnceId: "StopHardwareWorker"
; Remove scheduled task for autostart
Filename: "schtasks"; Parameters: "/delete /tn ""OmenCore"" /f"; Flags: runhidden; RunOnceId: "RemoveOmenCoreTask"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\\logs"
Type: dirifempty; Name: "{app}"

[Code]
function PawnIOInstallerExists: Boolean;
begin
  Result := FileExists(ExpandConstant('{src}\\PawnIO_setup.exe'));
end;

function IsPawnIOInstalled: Boolean;
var
  InstallPath: String;
begin
  Result := RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO', 'InstallLocation', InstallPath);
  if not Result then
    Result := DirExists(ExpandConstant('{pf}\\PawnIO'));
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpSelectTasks then
  begin
    if IsPawnIOInstalled then
    begin
      // PawnIO already installed, uncheck the task
      WizardForm.TasksList.Checked[2] := False;
    end;
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
end;

[Messages]
WelcomeLabel2=This will install [name/ver] on your computer.%n%n🎮 OmenCore - Complete OMEN Control Suite%n%n✓ FAN CONTROL - Custom curves, visual editor%n✓ MONITORING - CPU/GPU temps, fan RPM%n✓ POWER - Undervolting, Dynamic Boost%n✓ RGB - Keyboard lighting control%n✓ PROFILES - Auto-switch with games%n%nReplaces HP OMEN Gaming Hub.%n%nClick Next to continue.
