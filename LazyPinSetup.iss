#define MyAppName "LazyPin"
#define MyAppVersion "1.0.3"
#define MyAppExeName "LazyPin.exe"

[Setup]
AppId={{112800D9-F03C-4BC6-A049-47CB38A99617}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Raisul Sohan
AppPublisherURL=https://github.com/raisulsohan/LazyPin
AppSupportURL=https://github.com/raisulsohan/LazyPin
AppUpdatesURL=https://github.com/raisulsohan/LazyPin/releases
DefaultDirName={localappdata}\Programs\LazyPin
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=.
OutputBaseFilename=LazyPinSetup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile=LazyPin.ico
LicenseFile=LICENSE
VersionInfoVersion=1.0.3.0
VersionInfoCompany=Raisul Sohan
VersionInfoCopyright=Copyright (C) 2026 Raisul Sohan
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoDescription=Install {#MyAppName} by Raisul Sohan

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "startupicon"; Description: "Start LazyPin automatically when signing in"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "LazyPin.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "LazyPin.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "LazyPin.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "LazyPin.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "Start LazyPin.cmd"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/F /IM LazyPin.exe"; Flags: runhidden; RunOnceId: "StopLazyPin"
