#define MyAppName "LazyPin"
#define MyAppVersion "1.0.4"
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
VersionInfoVersion=1.0.4.0
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

[InstallDelete]
; Leftovers of the pre-rename "Pin to top" build. It used a different single-instance
; mutex, so with both startup shortcuts present two copies (two tray icons, two pin
; buttons) started at sign-in.
Type: files; Name: "{app}\PinToTop.exe"
Type: files; Name: "{app}\PinToTop.exe.config"
Type: files; Name: "{app}\PinToTop.ico"
Type: files; Name: "{app}\PinToTop.ps1"
Type: files; Name: "{app}\Start Pin to top.cmd"
Type: files; Name: "{userstartup}\Pin to top.lnk"
Type: files; Name: "{userprograms}\Pin to top.lnk"
Type: files; Name: "{userdesktop}\Pin to top.lnk"

[Code]
const
  WM_CLOSE = $0010;

// Ask a running LazyPin to exit cleanly: on WM_CLOSE it gives its pinned window the
// normal z-order back and removes its tray icon (a killed process leaves a ghost icon
// until the mouse passes over it). The pin button window carries a fixed title for
// this purpose. Force-kill only what is still there afterwards, including the
// pre-rename "Pin to top" build, which used a different single-instance mutex and
// could run next to LazyPin.
procedure StopLazyPin;
var
  Wnd: HWND;
  Tries, ResultCode: Integer;
begin
  Wnd := FindWindowByWindowName('LazyPin.PinButton');
  if Wnd <> 0 then
  begin
    Log('Asking the running LazyPin to exit');
    PostMessage(Wnd, WM_CLOSE, 0, 0);
    Tries := 0;
    while (FindWindowByWindowName('LazyPin.PinButton') <> 0) and (Tries < 50) do
    begin
      Sleep(100);
      Tries := Tries + 1;
    end;
    if FindWindowByWindowName('LazyPin.PinButton') = 0 then
      Log('LazyPin exited cleanly')
    else
      Log('LazyPin did not exit in time; forcing');
  end;
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM LazyPin.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM PinToTop.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopLazyPin;
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    StopLazyPin;
end;
