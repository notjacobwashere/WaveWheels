#ifndef AppVersion
  #define AppVersion "0.7.0"
#endif

[Setup]
AppId={{62CDFDE0-5349-4AB2-B7DE-95359B68E73F}
AppName=WaveWheels
AppVersion={#AppVersion}
AppPublisher=notjacobwashere
AppPublisherURL=https://github.com/notjacobwashere/WaveWheels
AppSupportURL=https://github.com/notjacobwashere/WaveWheels/issues
DefaultDirName={localappdata}\Programs\WaveWheels
DefaultGroupName=WaveWheels
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts
OutputBaseFilename=WaveWheels-{#AppVersion}-Setup
SetupIconFile=..\Source\Assets\WaveWheels.ico
UninstallDisplayIcon={app}\WaveWheels.exe
UninstallDisplayName=WaveWheels
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\LICENSE
CloseApplications=yes
CloseApplicationsFilter=WaveWheels.exe,WaveWheels.dll
RestartApplications=no
SetupLogging=yes

[Files]
Source: "..\artifacts\installer-app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\NOTICE"; DestDir: "{app}"; Flags: ignoreversion

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Icons]
Name: "{group}\WaveWheels"; Filename: "{app}\WaveWheels.exe"
Name: "{group}\Configure WaveWheels"; Filename: "{app}\WaveWheels.exe"; Parameters: "--settings"
Name: "{autodesktop}\WaveWheels"; Filename: "{app}\WaveWheels.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\WaveWheels.exe"; Parameters: "--settings"; Flags: nowait; Check: IsAppUpdate
Filename: "{app}\WaveWheels.exe"; Parameters: "--settings"; Description: "Open WaveWheels configuration"; Flags: nowait postinstall skipifsilent; Check: IsManualInstall

[Messages]
FinishedLabel=WaveWheels is installed. Use the system tray icon near the Windows clock to configure your wheels. Your settings are kept when upgrading or uninstalling.

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

function IsAppUpdate(): Boolean;
begin
  Result := ExpandConstant('{param:UPDATEFROMAPP|0}') <> '0';
end;

function IsManualInstall(): Boolean;
begin
  Result := not IsAppUpdate();
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
  AppPath: String;
begin
  Result := '';
  AppPath := ExpandConstant('{app}\WaveWheels.exe');
  if FileExists(AppPath) then
    Exec(AppPath, '--shutdown', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ExistingStartup: String;
begin
  if CurStep = ssPostInstall then begin
    // Preserve the user's opt-in while updating an existing registration to this location.
    if RegQueryStringValue(HKCU, RunKey, 'WaveWheels', ExistingStartup) then
      RegWriteStringValue(HKCU, RunKey, 'WaveWheels', '"' + ExpandConstant('{app}\WaveWheels.exe') + '"');
  end;
end;

function InitializeUninstall(): Boolean;
var
  ExitCode: Integer;
begin
  Exec(ExpandConstant('{app}\WaveWheels.exe'), '--shutdown', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ExistingStartup: String;
begin
  if CurUninstallStep = usUninstall then begin
    if RegQueryStringValue(HKCU, RunKey, 'WaveWheels', ExistingStartup) then
      if CompareText(ExistingStartup, '"' + ExpandConstant('{app}\WaveWheels.exe') + '"') = 0 then
        RegDeleteValue(HKCU, RunKey, 'WaveWheels');
  end;
  // AppData settings live outside {app} and are deliberately preserved.
end;
