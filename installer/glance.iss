; Glance installer (Inno Setup 6). Build with installer\build.ps1, which publishes the
; service and then runs:  iscc /DAppVersion=<v> /DSourceDir=<publish dir> installer\glance.iss
;
; Requires Inno Setup 6.3 or newer: the x64compatible architecture identifier and the
; ExecAndCaptureOutput support function used below are not available in older releases.

#if Ver < EncodeVer(6,3,0)
  #error Inno Setup 6.3 or newer is required to build the Glance installer
#endif

#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#ifndef SourceDir
  #define SourceDir "..\out\publish"
#endif

#define ServiceName "Glance"
#define ServiceExe "Glance.Service.exe"
; Must match IFirewall.RuleName in src/Glance.Platform.Windows/IFirewall.cs.
#define FirewallRuleName "Glance web UI"

[Setup]
; AppId identifies Glance to Windows for upgrades and uninstall. Never change it.
AppId={{B11BB4AD-5A77-4BA7-8412-FDE69D2F4CA9}
AppName=Glance
AppVersion={#AppVersion}
AppVerName=Glance {#AppVersion}
AppPublisher=Glance contributors
AppPublisherURL=https://github.com/gulshankumark/glance
AppSupportURL=https://github.com/gulshankumark/glance/issues
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
DefaultDirName={autopf}\Glance
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\out
OutputBaseFilename=Glance-Setup-{#AppVersion}
UninstallDisplayName=Glance
UninstallDisplayIcon={app}\{#ServiceExe}
WizardStyle=modern
Compression=lzma2
SolidCompression=yes

[Tasks]
Name: "startmenu"; Description: "Start-menu shortcut"; GroupDescription: "Shortcuts:"
Name: "desktopicon"; Description: "Desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
; Task 14 adds the "pawnio" task here (checked by default) together with its download step.

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Run]
; "Open Glance when setup finishes": opens the address the service itself reports (--print-url),
; so it follows the configured scheme and port. Runs as the original (non-elevated) user.
Filename: "{code:GetUiUrl}"; Description: "Open Glance when setup finishes"; Flags: postinstall shellexec skipifsilent nowait

[Code]
const
  DefaultUiUrl = 'http://localhost:8735/';
  ServiceStopTimeoutSeconds = 30;
  NotReadyExitCode = 10;

var
  UiUrl: String;
  ServiceNotReady: Boolean;

function ScExe: String;
begin
  Result := ExpandConstant('{sys}\sc.exe');
end;

function AppExe: String;
begin
  Result := ExpandConstant('{app}\{#ServiceExe}');
end;

function StartMenuShortcut: String;
begin
  { %ProgramData%\Microsoft\Windows\Start Menu\Programs\Glance.url (ShortcutSync, Task 8) }
  Result := ExpandConstant('{commonprograms}\Glance.url');
end;

function DesktopShortcut: String;
begin
  { %PUBLIC%\Desktop\Glance.url (ShortcutSync, Task 8) }
  Result := ExpandConstant('{commondesktop}\Glance.url');
end;

{ Runs a program hidden and waits. Returns its exit code, or -1 if it could not be started. }
function RunHidden(const FileName, Params: String): Integer;
var
  ResultCode: Integer;
begin
  Log('Running: "' + FileName + '" ' + Params);
  if Exec(FileName, Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := ResultCode
  else
    Result := -1;
  Log('Exit code: ' + IntToStr(Result));
end;

{ Runs a program hidden and returns its standard output joined into one string. }
function RunAndCapture(const FileName, Params: String; var Code: Integer): String;
var
  Output: TExecOutput;
  I: Integer;
begin
  Result := '';
  Code := -1;
  if ExecAndCaptureOutput(FileName, Params, '', SW_HIDE, ewWaitUntilTerminated, Code, Output) then
  begin
    for I := 0 to GetArrayLength(Output.StdOut) - 1 do
      Result := Result + Output.StdOut[I] + #13#10;
  end
  else
    Code := -1;
end;

function ServiceExists: Boolean;
begin
  { sc query exits 0 for an installed service and 1060 when it does not exist. }
  Result := RunHidden(ScExe, 'query {#ServiceName}') = 0;
end;

function ServiceStopped: Boolean;
var
  Code: Integer;
  State: String;
begin
  State := RunAndCapture(ScExe, 'query {#ServiceName}', Code);
  Result := (Code <> 0) or (Pos('STOPPED', State) > 0);
end;

{ Stops the service if it exists and waits (bounded) until it has stopped. }
function StopService: Boolean;
var
  I: Integer;
begin
  Result := True;
  if not ServiceExists then
    Exit;
  RunHidden(ScExe, 'stop {#ServiceName}');
  for I := 1 to ServiceStopTimeoutSeconds * 2 do
  begin
    if ServiceStopped then
      Exit;
    Sleep(500);
  end;
  Result := ServiceStopped;
end;

{ The local UI address derived from current settings (scheme + port), as the service reports it. }
function QueryUiUrl: String;
var
  Code: Integer;
begin
  Result := Trim(RunAndCapture(AppExe, '--print-url', Code));
  if (Code <> 0) or (Result = '') then
  begin
    Log('--print-url failed; using ' + DefaultUiUrl);
    Result := DefaultUiUrl;
  end;
end;

function GetUiUrl(Param: String): String;
begin
  if UiUrl = '' then
    UiUrl := QueryUiUrl;
  Result := UiUrl;
end;

{ Writes or removes one .url shortcut. The service keeps existing ones up to date but never creates them. }
procedure SyncShortcut(const Path: String; const Wanted: Boolean);
begin
  if Wanted then
  begin
    if not SaveStringToFile(Path, '[InternetShortcut]'#13#10 + 'URL=' + UiUrl + #13#10, False) then
      Log('Could not write shortcut ' + Path);
  end
  else if FileExists(Path) then
    DeleteFile(Path);
end;

procedure RegisterAndStartService;
var
  BinPath: String;
begin
  { The image path contains spaces (Program Files), so it is quoted inside the binPath= value. }
  BinPath := 'binPath= "\"' + AppExe + '\""';
  if ServiceExists then
    RunHidden(ScExe, 'config {#ServiceName} ' + BinPath + ' start= auto obj= LocalSystem DisplayName= "Glance"')
  else
    RunHidden(ScExe, 'create {#ServiceName} ' + BinPath + ' start= auto obj= LocalSystem DisplayName= "Glance"');
  RunHidden(ScExe, 'description {#ServiceName} "Drives the Glance LCD screen and serves the local web UI."');
  RunHidden(ScExe, 'start {#ServiceName}');
end;

{ Upgrade: stop the running service so its files can be replaced. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not StopService then
    Result := 'The Glance service did not stop within ' + IntToStr(ServiceStopTimeoutSeconds) +
      ' seconds. Stop it in Services (services.msc) and run setup again.';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Msg: String;
begin
  if CurStep <> ssPostInstall then
    Exit;

  { 1. Hardened %ProgramData%\Glance before the service first runs. An existing directory is left
       as it is; the service is the authority on it. }
  WizardForm.StatusLabel.Caption := 'Preparing the data directory...';
  if RunHidden(AppExe, '--prepare-data-dir') <> 0 then
    Log('--prepare-data-dir failed; the service will report the data directory problem.');

  { 2. Shortcuts point at the address from current settings. }
  UiUrl := QueryUiUrl;
  Log('UI address: ' + UiUrl);
  SyncShortcut(StartMenuShortcut, WizardIsTaskSelected('startmenu'));
  SyncShortcut(DesktopShortcut, WizardIsTaskSelected('desktopicon'));

  { 3. Register (or reconfigure on upgrade) and start the service, then wait for it to answer. }
  WizardForm.StatusLabel.Caption := 'Starting the Glance service...';
  RegisterAndStartService;
  if RunHidden(AppExe, '--wait-ready 30') <> 0 then
  begin
    { No rollback: the files and service stay installed; the log says what went wrong. }
    ServiceNotReady := True;
    Msg := 'Glance installed but did not answer within 30 s. See ' +
      ExpandConstant('{commonappdata}') + '\Glance\logs.';
    Log(Msg);
    if not WizardSilent then
      MsgBox(Msg, mbError, MB_OK);
  end;
end;

{ Silent installs report a service that never answered with a non-zero exit code. }
function GetCustomSetupExitCode: Integer;
begin
  if ServiceNotReady then
    Result := NotReadyExitCode
  else
    Result := 0;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    { Before files are removed: stop and delete the service, remove the firewall rule (if the
      service created one) and the shortcuts. PawnIO is never removed; other tools use it. }
    StopService;
    if ServiceExists then
      RunHidden(ScExe, 'delete {#ServiceName}');
    RunHidden(ExpandConstant('{sys}\netsh.exe'), 'advfirewall firewall delete rule name="{#FirewallRuleName}"');
    DeleteFile(StartMenuShortcut);
    DeleteFile(DesktopShortcut);
  end
  else if CurUninstallStep = usPostUninstall then
  begin
    { Settings, layouts and logs: ask in guided mode, always keep in silent mode. }
    DataDir := ExpandConstant('{commonappdata}\Glance');
    if (not UninstallSilent) and DirExists(DataDir) then
      if MsgBox('Also delete Glance''s settings, layouts and logs in ' + DataDir + '?',
          mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
