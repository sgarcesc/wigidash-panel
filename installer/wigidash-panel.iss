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
#define ManagerExe "{commonpf32}\G.SKILL\WigiDash Manager\WigiDashManager.exe"

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

[Run]
; Skipped when the Manager isn't at its default location. Not skipifsilent: a silent install still
; closes the Manager (CloseApplications), so it must start it again too.
Filename: "{#ManagerExe}"; WorkingDir: "{commonpf32}\G.SKILL\WigiDash Manager"; \
  Description: "Start WigiDash Manager"; Flags: postinstall nowait; \
  Check: ManagerInstalled

[Code]
function ManagerInstalled: Boolean;
begin
  Result := FileExists(ExpandConstant('{#ManagerExe}'));
end;

// The uninstaller doesn't use Restart Manager, so close the Manager here; otherwise the loaded
// widget DLL is locked and can't be removed.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
    if Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM WigiDashManager.exe', '', SW_HIDE,
        ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) then
      // The DLL can stay locked for a few seconds after the process exits.
      Sleep(3000);
end;

[Messages]
FinishedLabel=Setup has installed [name] into WigiDash Manager.%n%nIn WigiDash Manager, add a page and drop Sensor Panel onto it at size 5x4.
