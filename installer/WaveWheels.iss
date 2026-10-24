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
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{62CDFDE0-5349-4AB2-B7DE-95359B68E73F}_is1';

var
  IntroControlsPage, IntroSettingsPage: TOutputMsgWizardPage;
  SkipIntroButton: TNewButton;
  IntroEligible, IntroSkipped: Boolean;

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

procedure SkipIntroClick(Sender: TObject);
begin
  IntroSkipped := True;
  Log('WaveWheels intro: skipped by user');
  WizardForm.NextButton.OnClick(WizardForm.NextButton);
end;

procedure InitializeWizard();
begin
  // Read existing installation/settings state only. Onboarding never writes user preferences.
  IntroEligible := not IsAppUpdate() and not HasExistingWaveWheels();
  if IntroEligible then Log('WaveWheels intro: first install')
    else Log('WaveWheels intro: existing install or settings');
  if WizardSilent() then Log('WaveWheels intro: silent skip');

  IntroControlsPage := CreateOutputMsgPage(wpWelcome, 'Meet WaveWheels',
    'Your mix, one scroll away.',
    'WaveWheels runs quietly in the system tray. Without Wave Link, one wheel controls your Windows output volume. Connect Wave Link to use multiple channel wheels.' + #13#10#13#10 +
    'REVEAL AND ADJUST' + #13#10 +
    'Scroll at the left screen edge to bring out your wheels. The opening scroll does not change volume. Pause briefly, move over a wheel, then scroll again to adjust it.' + #13#10#13#10 +
    'Drag a wheel up or down to adjust it, or click its speaker button to mute. Scrolling at the very edge only opens the wheels.' + #13#10#13#10 +
    'Configuration opens after Setup. Closing it keeps WaveWheels running; use the tray icon near the clock to open it again.');

  IntroSettingsPage := CreateOutputMsgPage(IntroControlsPage.ID, 'Make it yours',
    'A quick first setup, then tune it your way.',
    '1. Customize the Windows volume wheel, or open Wave Link and bind multiple wheels to its input channels and mixes. Wave Link wheels return automatically when connected.' + #13#10 +
    '2. Choose Preview wheels and try scrolling over a wheel.' + #13#10#13#10 +
    'Profiles - save layouts and optionally switch for focused apps.' + #13#10 +
    'Blacklist - pause wheels while selected apps are focused.' + #13#10 +
    'Scroll - tune normal steps, acceleration, and hide delay.' + #13#10 +
    'Shift - snap in larger increments (20% default); optional dots.' + #13#10 +
    'Style - choose names, colors, and size. Wave Link also unlocks multiple wheels and reordering.' + #13#10 +
    'Limits - cap wheel volume; external mixer controls can exceed it.' + #13#10 +
    'Placement - choose screen edge, display, position, and spacing.' + #13#10 +
    'Updates - check, download, and install from the app.' + #13#10#13#10 +
    'Changes save automatically. Sign-in startup is optional.');

  SkipIntroButton := TNewButton.Create(WizardForm);
  SkipIntroButton.Parent := WizardForm;
  SkipIntroButton.Caption := 'Skip intro';
  SkipIntroButton.Left := ScaleX(16);
  SkipIntroButton.Top := WizardForm.NextButton.Top;
  SkipIntroButton.Width := ScaleX(90);
  SkipIntroButton.Height := WizardForm.NextButton.Height;
  SkipIntroButton.Visible := False;
  SkipIntroButton.OnClick := @SkipIntroClick;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := ((PageID = IntroControlsPage.ID) or (PageID = IntroSettingsPage.ID)) and
    (not IntroEligible or IntroSkipped or WizardSilent());
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  SkipIntroButton.Visible := (CurPageID = IntroControlsPage.ID) or (CurPageID = IntroSettingsPage.ID);
  if CurPageID = IntroControlsPage.ID then Log('WaveWheels intro: controls page shown');
  if CurPageID = IntroSettingsPage.ID then Log('WaveWheels intro: settings page shown');
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
