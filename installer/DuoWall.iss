; Script generated for DuoWall by SRMD v2.0.0
#define MyAppName "DuoWall by SRMD"
#define MyAppVersion "2.0.0"
#define MyAppPublisher "Syed Sarmad Iftikhar"
#define MyAppURL "https://github.com/xarmad19/DuoWall"
#define MyAppExeName "DuoWall.exe"

[Setup]
AppId={{5A8C3D21-7B32-4E9A-A372-F9D1E4E8D201}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\DuoWall
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=..\publish
OutputBaseFilename=DuoWall-v2.0.0-Setup
SetupIconFile=..\src\DuoWall.App\Assets\duowall.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Start DuoWall automatically on Windows login"; GroupDescription: "Preferences:"; Flags: unchecked

[Files]
Source: "..\publish\DuoWall-Portable\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\DuoWall-Portable\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "{#MyAppExeName},*.pdb"
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{app}\{#MyAppExeName}"
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
