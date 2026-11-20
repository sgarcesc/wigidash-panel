; Inno Setup script for the Sensor Panel widget. scripts\package.ps1 -Installer compiles it with
;   ISCC.exe /DVersion=<x.y.z> /DSourceDir=<staged folder> /DOutputDir=<dist> wigidash-panel.iss
; SourceDir is the folder package.ps1 stages (the same files as the release zip).

#ifndef Version
  #error Pass /DVersion=<x.y.z>
#endif
#ifndef SourceDir
  #error Pass /DSourceDir=<staged folder from scripts\package.ps1>
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

; WigiDash Manager loads Widgets\<GUID>\<GUID>.dll, so the GUID is also the folder and DLL name.
#define Guid "56507051-FDDD-447B-B59B-81D78C7CD961"
#define ManagerDir "{commonpf32}\G.SKILL\WigiDash Manager"
#define ManagerExe ManagerDir + "\WigiDashManager.exe"

[Setup]
; Fixed AppId, so a newer installer upgrades the existing install in place. Never change it.
AppId={{8D4F2C1A-6B3E-4F7D-9A51-2E0C7B6D4A93}
AppName=WigiDash Sensor Panel
AppVersion={#Version}
AppVerName=WigiDash Sensor Panel {#Version}
AppPublisher=sgarcesc
AppPublisherURL=https://github.com/sgarcesc/wigidash-panel
AppSupportURL=https://github.com/sgarcesc/wigidash-panel/issues
VersionInfoVersion={#Version}
; The widget folder is under %APPDATA%, so no admin rights and no UAC prompt.
PrivilegesRequired=lowest
DefaultDirName={userappdata}\G.SKILL\WigiDashManager\Widgets\{#Guid}
DisableDirPage=yes
; The Manager only looks in this folder, so never reuse a different one from an earlier install.
UsePreviousAppDir=no
DisableProgramGroupPage=yes
DisableReadyPage=yes
DisableWelcomePage=no
Uninstallable=yes
CreateUninstallRegKey=yes
UninstallDisplayName=WigiDash Sensor Panel
; The Manager keeps the widget DLL loaded. Restart Manager closes it before files are replaced
; (and retries or prompts if the DLL stays locked); the [Run] entry below starts it again.
CloseApplications=force
RestartApplications=no
OutputDir={#OutputDir}
OutputBaseFilename=wigidash-panel-setup-{#Version}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Files]
Source: "{#SourceDir}\{#Guid}.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\{#Guid}.pdb"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#SourceDir}\*.png"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
; Shipped layouts (default.json included) are overwritten on every install, like install.ps1 does.
; Uninstall removes only these files; skins and fonts the user added stay where they are.
Source: "{#SourceDir}\layouts\*"; DestDir: "{app}\layouts"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\fonts\*"; DestDir: "{app}\fonts"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

[Dirs]
; Drop .ttf/.otf files here to make them available to every layout and AIDA64 skin.
; Removed on uninstall only if it's empty.
Name: "{app}\fonts"

[UninstallDelete]
; Written by the widget at runtime, so not in [Files]. Without these the widget folder can never be
; removed. Only these two files: the user's own skins and fonts keep their folders.
Type: files; Name: "{app}\sensors.txt"
Type: files; Name: "{app}\layouts\live-frame.png"

[Run]
; Skipped when the Manager isn't at its default location, and in silent installs when the Manager
; wasn't running beforehand. Not skipifsilent: a silent upgrade still closes the Manager through
; Restart Manager, so it has to start it again. shellexec goes through ShellExecute like
; install.ps1's Start-Process, so a UAC prompt would appear if the Manager ever needed elevation.
Filename: "{#ManagerExe}"; WorkingDir: "{#ManagerDir}"; \
  Description: "Start WigiDash Manager"; Flags: postinstall nowait shellexec; \
  Check: ShouldStartManager

[Code]
var
  ManagerWasRunning: Boolean;

function ManagerInstalled: Boolean;
begin
  Result := FileExists(ExpandConstant('{#ManagerExe}'));
end;

function GetCurrentProcessId: DWORD;
  external 'GetCurrentProcessId@kernel32.dll stdcall';
function ProcessIdToSessionId(ProcessId: DWORD; var SessionId: DWORD): BOOL;
  external 'ProcessIdToSessionId@kernel32.dll stdcall';

// This user's Windows session. Other users' Managers (fast user switching) live in other sessions;
// this installer can neither close nor restart those, so they must not count.
function CurrentSession: String;
var
  SessionId: DWORD;
begin
  if not ProcessIdToSessionId(GetCurrentProcessId, SessionId) then SessionId := 0;
  Result := IntToStr(SessionId);
end;

// Assumes the Manager is running when WMI can't be queried; the callers then do one harmless
// extra step (a start, or a taskkill that finds nothing) instead of skipping a needed one.
function IsManagerRunning: Boolean;
var
  Locator, Service, Processes: Variant;
begin
  Result := True;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('.', 'root\CIMV2');
    Processes := Service.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = ''WigiDashManager.exe'''
      + ' AND SessionId = ' + CurrentSession);
    Result := Processes.Count > 0;
  except
  end;
end;

// Runs before Restart Manager closes anything, so this is the state the user left it in.
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  ManagerWasRunning := IsManagerRunning;
  Result := '';
end;

// Interactive installs always offer the checkbox; silent installs only restart a Manager they closed.
function ShouldStartManager: Boolean;
begin
  Result := ManagerInstalled and (ManagerWasRunning or not WizardSilent);
end;

// The uninstaller doesn't use Restart Manager, so the Manager is closed at usUninstall; otherwise
// the loaded widget DLL is locked and can't be removed. Interactive uninstalls warn about this in
// Inno's own confirmation (ConfirmUninstall below), so there's only one dialog.
function InitializeUninstall: Boolean;
begin
  ManagerWasRunning := IsManagerRunning;
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if not ManagerWasRunning then Exit;
  if CurUninstallStep = usUninstall then begin
    if Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM WigiDashManager.exe /FI "SESSION eq '
        + CurrentSession + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) then
      // The DLL can stay locked for a few seconds after the process exits.
      Sleep(3000);
  end else if (CurUninstallStep = usPostUninstall) and ManagerInstalled then
    ShellExec('', ExpandConstant('{#ManagerExe}'), '', ExpandConstant('{#ManagerDir}'),
      SW_SHOWNORMAL, ewNoWait, ResultCode);
end;

[Messages]
ConfirmUninstall=Are you sure you want to completely remove %1 and all of its components?%n%nIf WigiDash Manager is running, it will be closed while the widget is removed and started again afterwards. Save any unsaved changes in it first.
FinishedLabel=Setup has installed [name] into WigiDash Manager.%n%nIn WigiDash Manager, add a page and drop Sensor Panel onto it at size 5x4.
