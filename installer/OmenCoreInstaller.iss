#define MyAppName "OmenCore"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0.8"
#endif
#define MyAppPublisher "OmenCore Project"
#define MyAppExeName "OmenCore.exe"
#define LibreHWMonitorExe "LibreHardwareMonitor.exe"

[Setup]
AppId={{6F5B6F3F-8FAF-4FC8-A5E0-4E2C0E8F2E2B}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL="https://github.com/theantipopau/omencore"
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
SetupIconFile=..\src\OmenCoreApp\Assets\OmenCore.ico
Compression=lzma
SolidCompression=yes
OutputDir=..\\artifacts
OutputBaseFilename=OmenCoreSetup-{#MyAppVersion}
PrivilegesRequired=admin
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "installdriver"; Description: "Install WinRing0 driver for fan control and undervolting (Recommended)"; GroupDescription: "Hardware Control:"; Flags: checkedonce

[Files]
Source: "..\\publish\\win-x64\\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Bundle LibreHardwareMonitor (if available in installer directory)
; Commented out: LibreHardwareMonitor not bundled - user can install separately
; Source: "LibreHardwareMonitor\\*"; DestDir: "{app}\\LibreHardwareMonitor"; Flags: ignoreversion recursesubdirs createallsubdirs; Tasks: installdriver; Check: LibreHardwareMonitorExists

[Icons]
Name: "{autoprograms}\\{#MyAppName}"; Filename: "{app}\\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\\{#MyAppName}"; Filename: "{app}\\{#MyAppExeName}"; Tasks: desktopicon; WorkingDir: "{app}"

[Run]
; Install WinRing0 driver by running LibreHardwareMonitor once
; Commented out: LibreHardwareMonitor not bundled - see README.md for manual installation
; Filename: "{app}\\LibreHardwareMonitor\\{#LibreHWMonitorExe}"; Parameters: "/minimize"; WorkingDir: "{app}\\LibreHardwareMonitor"; StatusMsg: "Installing WinRing0 driver..."; Flags: waituntilterminated runhidden; Tasks: installdriver; Check: LibreHardwareMonitorExists
Filename: "{app}\\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Code]
function LibreHardwareMonitorExists: Boolean;
begin
  Result := FileExists(ExpandConstant('{srcexe}\\..\\LibreHardwareMonitor\\{#LibreHWMonitorExe}'));
end;

[Messages]
WelcomeLabel2=This will install [name/ver] on your computer.%n%nFor full hardware control (fan curves and CPU undervolting), the WinRing0 driver will be installed. This requires administrator privileges.
