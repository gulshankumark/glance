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
; Always write a setup log (in %TEMP%); failure messages point to it via {log}.
SetupLogging=yes

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
  { Custom exit code of a silent install whose service did not register or answer (spec section 3.4). }
  NotReadyExitCode = 10;

var
  UiUrl: String;
  { Files are in place but the service did not register or did not answer in time. }
  InstallIncomplete: Boolean;
  { PrepareToInstall stopped a running service (upgrade); restart it if setup does not finish. }
  StoppedForUpgrade: Boolean;
  { Set at ssPostInstall: the new files are installed. }
  InstallFinished: Boolean;

function ScExe: String;
begin
  Result := ExpandConstant('{sys}\sc.exe');
end;

function AppExe: String;
begin
  Result := ExpandConstant('{app}\{#ServiceExe}');
end;

{ %ProgramData%\Microsoft\Windows\Start Menu\Programs\Glance.url (ShortcutSync, Task 8) }
function StartMenuShortcut: String;
begin
  Result := ExpandConstant('{commonprograms}\Glance.url');
end;

{ %PUBLIC%\Desktop\Glance.url (ShortcutSync, Task 8) }
function DesktopShortcut: String;
begin
  Result := ExpandConstant('{commondesktop}\Glance.url');
end;

function SetupLogPath: String;
begin
  Result := ExpandConstant('{log}');
  if Result = '' then
    Result := 'the setup log in %TEMP%';
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

{ Runs a program hidden and returns its standard output, one line per output line. }
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

{ sc query exits 0 for an installed service and 1060 when it does not exist. }
function ServiceExists: Boolean;
begin
  Result := RunHidden(ScExe, 'query {#ServiceName}') = 0;
end;

{ True if the STATE line of sc query output reports STOPPED, by name or by its numeric value 1,
  e.g. "        STATE              : 1  STOPPED". }
function StateIsStopped(const Output: String): Boolean;
var
  Rest, Line: String;
  P: Integer;
begin
  Result := False;
  Rest := Output;
  while Rest <> '' do
  begin
    P := Pos(#10, Rest);
    if P = 0 then
    begin
      Line := Rest;
      Rest := '';
    end
    else
    begin
      Line := Copy(Rest, 1, P - 1);
      Rest := Copy(Rest, P + 1, Length(Rest));
    end;
    if Pos('STATE', Uppercase(Line)) > 0 then
    begin
      Result := (Pos(': 1 ', Line + ' ') > 0) or (Pos('STOPPED', Uppercase(Line)) > 0);
      Exit;
    end;
  end;
end;

function ServiceStopped: Boolean;
var
  Code: Integer;
  State: String;
begin
  State := RunAndCapture(ScExe, 'query {#ServiceName}', Code);
  Result := (Code <> 0) or StateIsStopped(State);
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

{ Accepts only a single non-empty http(s) line without control characters. }
function IsAcceptableUrl(const Url: String): Boolean;
var
  I: Integer;
  Lower: String;
begin
  Result := False;
  Lower := Lowercase(Url);
  if (Pos('http://', Lower) <> 1) and (Pos('https://', Lower) <> 1) then
    Exit;
  if (Lower = 'http://') or (Lower = 'https://') then
    Exit;
  { Rejects CR/LF (more than one line) and any other control character. }
  for I := 1 to Length(Url) do
    if (Ord(Url[I]) < 32) or (Ord(Url[I]) = 127) then
      Exit;
  Result := True;
end;

{ The local UI address derived from current settings (scheme + port), as the service reports it. }
function QueryUiUrl: String;
var
  Code: Integer;
begin
  Result := Trim(RunAndCapture(AppExe, '--print-url', Code));
  if (Code <> 0) or not IsAcceptableUrl(Result) then
  begin
    Log('--print-url failed or returned an unusable value (exit ' + IntToStr(Code) + '); using ' + DefaultUiUrl);
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
procedure SyncShortcut(const Path: String; Wanted: Boolean);
begin
  if Wanted then
  begin
    if not SaveStringToFile(Path, '[InternetShortcut]'#13#10 + 'URL=' + UiUrl + #13#10, False) then
      Log('Could not write shortcut ' + Path);
  end
  else if FileExists(Path) then
    DeleteFile(Path);
end;

{ The install folder must be inside Program Files: the service runs as LocalSystem, and folders
  elsewhere (e.g. C:\Glance) inherit write access for ordinary users. Both paths are normalised
  and end in a backslash, so "C:\Program FilesX\..." does not match. }
function IsUnderProgramFiles(const Dir: String): Boolean;
var
  Root, Candidate: String;
begin
  Root := Lowercase(AddBackslash(ExpandFileName(ExpandConstant('{commonpf}'))));
  Candidate := Lowercase(AddBackslash(ExpandFileName(Dir)));
  Result := (Length(Candidate) > Length(Root)) and (Copy(Candidate, 1, Length(Root)) = Root);
end;

function ProgramFilesMessage: String;
begin
  Result := 'Glance must be installed in a folder inside ' + ExpandConstant('{commonpf}') +
    ', because its service runs as SYSTEM and folders elsewhere may be writable by ordinary users.';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpSelectDir) and not IsUnderProgramFiles(WizardDirValue) then
  begin
    MsgBox(ProgramFilesMessage, mbError, MB_OK);
    Result := False;
  end;
end;

{ Returns False if sc create/config failed. sc start is not checked: --wait-ready covers it. }
function RegisterAndStartService: Boolean;
var
  BinPath: String;
  Code: Integer;
begin
  { The image path contains spaces (Program Files), so it is quoted inside the binPath= value. }
  BinPath := 'binPath= "\"' + AppExe + '\""';
  if ServiceExists then
    Code := RunHidden(ScExe, 'config {#ServiceName} ' + BinPath + ' start= auto obj= LocalSystem DisplayName= "Glance"')
  else
    Code := RunHidden(ScExe, 'create {#ServiceName} ' + BinPath + ' start= auto obj= LocalSystem DisplayName= "Glance"');
  Result := Code = 0;
  if not Result then
  begin
    Log('Registering the service failed (sc exit ' + IntToStr(Code) + ').');
    Exit;
  end;
  RunHidden(ScExe, 'description {#ServiceName} "Drives the Glance LCD screen and serves the local web UI."');
  RunHidden(ScExe, 'start {#ServiceName}');
end;

{ Rejects install folders outside Program Files (again here, because /DIR= in silent mode skips the
  folder page), then, on upgrade, stops the running service so its files can be replaced. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not IsUnderProgramFiles(ExpandConstant('{app}')) then
  begin
    Result := ProgramFilesMessage;
    Log(Result);
    Exit;
  end;
  if ServiceExists and not ServiceStopped then
    StoppedForUpgrade := True;
  if not StopService then
    Result := 'The Glance service did not stop within ' + IntToStr(ServiceStopTimeoutSeconds) +
      ' seconds. Stop it in Services (services.msc) and run setup again.';
end;

{ Files and service stay installed (no rollback); guided mode explains, silent mode exits non-zero. }
procedure ReportIncomplete(const Msg: String);
begin
  InstallIncomplete := True;
  Log(Msg);
  if not WizardSilent then
    MsgBox(Msg, mbError, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep <> ssPostInstall then
    Exit;
  InstallFinished := True;

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
  if not RegisterAndStartService then
  begin
    ReportIncomplete('Glance was installed, but its service could not be registered. See ' + SetupLogPath + '.');
    Exit;
  end;
  if RunHidden(AppExe, '--wait-ready 30') <> 0 then
    ReportIncomplete('Glance installed but did not answer within 30 s. See ' +
      ExpandConstant('{commonappdata}') + '\Glance\logs.');
end;

{ Silent installs report a service that was not registered or never answered with a non-zero exit code. }
function GetCustomSetupExitCode: Integer;
begin
  if InstallIncomplete then
    Result := NotReadyExitCode
  else
    Result := 0;
end;

{ A cancelled or failed upgrade must not leave the previously running service stopped. }
procedure DeinitializeSetup;
begin
  if StoppedForUpgrade and not InstallFinished and ServiceExists then
  begin
    Log('Setup did not finish; restarting the previously running service.');
    RunHidden(ScExe, 'start {#ServiceName}');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    { Before files are removed: stop and delete the service, remove the firewall rule (if the
      service created one) and the shortcuts. PawnIO is never removed; other tools use it. }
    if ServiceExists then
    begin
      if not StopService then
      begin
        Log('The Glance service did not stop within ' + IntToStr(ServiceStopTimeoutSeconds) +
          ' seconds; some files may remain until a reboot.');
        if not UninstallSilent then
          MsgBox('The Glance service did not stop in time. Some files may remain in the install folder until you restart Windows.',
            mbInformation, MB_OK);
      end;
      RunHidden(ScExe, 'delete {#ServiceName}');
    end;
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
