#define MyAppName "Pin to top"
#define MyAppVersion "1.0.3"
#define MyAppExeName "PinToTop.exe"

[Setup]
AppId={{112800D9-F03C-4BC6-A049-47CB38A99617}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Raisul Sohan
AppPublisherURL=https://github.com/raisulsohan/PinToTop
AppSupportURL=https://github.com/raisulsohan/PinToTop
AppUpdatesURL=https://github.com/raisulsohan/PinToTop/releases
DefaultDirName={localappdata}\Programs\Pin to top
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=.
OutputBaseFilename=PinToTopSetup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile=PinToTop.ico
LicenseFile=LICENSE
VersionInfoVersion=1.0.3.0
VersionInfoCompany=Raisul Sohan
VersionInfoCopyright=Copyright (C) 2026 Raisul Sohan
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoDescription=Install {#MyAppName} by Raisul Sohan

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "startupicon"; Description: "Start Pin to top automatically when signing in"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "PinToTop.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "PinToTop.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "PinToTop.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "PinToTop.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "Start Pin to top.cmd"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/F /IM PinToTop.exe"; Flags: runhidden; RunOnceId: "StopPinToTop"
