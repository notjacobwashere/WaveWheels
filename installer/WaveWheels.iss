#ifndef AppVersion
  #define AppVersion "0.10.0-beta.1"
#endif
#ifndef AppNumericVersion
  #define AppNumericVersion "0.10.0.0"
#endif

[Setup]
AppId={{62CDFDE0-5349-4AB2-B7DE-95359B68E73F}
AppName=WaveWheels
AppVersion={#AppVersion}
VersionInfoVersion={#AppNumericVersion}
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
#if Ver >= EncodeVer(6,6,0)
WizardStyle=modern dark
#else
WizardStyle=modern
#endif
WizardSizePercent=120
DisableWelcomePage=yes
DisableReadyPage=yes
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
Filename: "{app}\WaveWheels.exe"; Parameters: "--setup"; Description: "Set up WaveWheels"; Flags: nowait postinstall skipifsilent; Check: IsFirstManualInstall
Filename: "{app}\WaveWheels.exe"; Parameters: "--settings"; Description: "Open WaveWheels"; Flags: nowait postinstall skipifsilent; Check: IsExistingManualInstall

[LangOptions]
DialogFontName=Segoe UI
DialogFontSize=10

[Messages]
FinishedLabel=WaveWheels is ready. The setup guide will help you choose placement and try the controls. Open configuration later from the system tray. Your settings are preserved when updating or uninstalling.

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{62CDFDE0-5349-4AB2-B7DE-95359B68E73F}_is1';

var
  IntroPage: TWizardPage;
  IntroEligible: Boolean;

function IsAppUpdate(): Boolean;
begin
  Result := ExpandConstant('{param:UPDATEFROMAPP|0}') <> '0';
end;

function IsManualInstall(): Boolean;
begin
  Result := not IsAppUpdate();
end;

function HasExistingWaveWheels(): Boolean;
begin
  Result := RegKeyExists(HKCU64, UninstallKey) or RegKeyExists(HKCU32, UninstallKey) or
    FileExists(ExpandConstant('{localappdata}\WaveWheels\settings.json'));
end;

function IsFirstManualInstall(): Boolean;
begin
  Result := IsManualInstall() and IntroEligible;
end;

function IsExistingManualInstall(): Boolean;
begin
  Result := IsManualInstall() and not IntroEligible;
end;

procedure IntroText(Top, Height, Size: Integer; Caption: String; Accent: Boolean);
var
  LabelControl: TNewStaticText;
begin
  LabelControl := TNewStaticText.Create(WizardForm);
  LabelControl.Parent := IntroPage.Surface;
  // Size against the actual page surface, including font and DPI scaling.
  LabelControl.SetBounds(ScaleX(22), IntroPage.SurfaceHeight * Top div 100,
    IntroPage.SurfaceWidth - ScaleX(44), IntroPage.SurfaceHeight * Height div 100);
  LabelControl.AutoSize := False;
  LabelControl.WordWrap := True;
  LabelControl.Caption := Caption;
  LabelControl.Font.Name := 'Segoe UI';
  LabelControl.Font.Size := Size;
  LabelControl.Font.Color := $00C2AD98;
  if Accent then begin
    LabelControl.Font.Color := $00D9E6A3;
    LabelControl.Font.Style := [fsBold];
  end;
end;

procedure InitializeWizard();
begin
  // Existing installations and in-app updates bypass the first-run introduction.
  IntroEligible := not IsAppUpdate() and not HasExistingWaveWheels();
  if IntroEligible then Log('WaveWheels setup: first install')
    else Log('WaveWheels setup: existing install or settings');
  if WizardSilent() then Log('WaveWheels setup: silent skip');
  IntroPage := CreateCustomPage(wpWelcome, 'Install WaveWheels', 'Windows audio and Wave Link controls');
  IntroPage.Surface.Color := $00201810;
  IntroText(4, 12, 20, 'Audio controls at the screen edge', True);
  IntroText(19, 13, 11, 'Control Windows Audio immediately. Connect Wave Link to use multiple channel wheels.', False);
  IntroText(36, 7, 12, '1   Install', True);
  IntroText(44, 8, 10, 'Installs for your Windows account. No administrator access required.', False);
  IntroText(58, 7, 12, '2   Choose your placement', True);
  IntroText(66, 9, 10, 'A short guide opens after installation. Pick an edge and try a sample wheel.', False);
  IntroText(80, 7, 12, '3   Use the wheels', True);
  IntroText(88, 10, 10, 'Scroll at the edge. Reopen configuration from the system tray.', False);
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = IntroPage.ID) and (not IntroEligible or WizardSilent());
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = IntroPage.ID then Log('WaveWheels setup: introduction shown');
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
