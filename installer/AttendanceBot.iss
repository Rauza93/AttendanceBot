#define MyAppName "AttendanceBot"
#define MyAppVersion "1.0.0"
#define MyAppExeName "AttendanceBot.App.exe"
[Setup]
AppId={{B4A1B8E1-4F1E-45DE-9B8A-7D3C4C8E2A11}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\AttendanceBot
DefaultGroupName={#MyAppName}
OutputBaseFilename=AttendanceBot-Setup-v{#MyAppVersion}
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
Uninstallable=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
[Icons]
Name: "{group}\AttendanceBot"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\AttendanceBot"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked
[Dirs]
Name: "{commonappdata}\AttendanceBot\Logs"
Name: "{commonappdata}\AttendanceBot\Screenshots"
[Run]
Filename: "schtasks.exe"; Parameters: "/Create /TN ""AttendanceBot Startup"" /TR ""{app}\AttendanceBot.Worker.exe"" /SC ONLOGON /RL LIMITED /F"; Flags: runhidden
[UninstallDelete]
Type: filesandordirs; Name: "{app}"
; Application data under ProgramData is intentionally preserved. Remove it manually if desired.
