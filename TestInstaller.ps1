# Runs only on a disposable GitHub Actions runner: installation tests touch its user profile.
param()
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Installer smoke tests must run on a disposable GitHub Actions runner.' }
Push-Location -LiteralPath $PSScriptRoot
try {
    [xml]$project = Get-Content -LiteralPath 'Source/WaveWheels.csproj' -Raw
    $version = [string]$project.Project.PropertyGroup.Version
    $setup = Join-Path $PSScriptRoot "artifacts/WaveWheels-$version-Setup.exe"
    $installDir = Join-Path $env:RUNNER_TEMP 'WaveWheels Installer Smoke'
    $profile = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'WaveWheels'
    # Exercise the real first-install pages only on this disposable runner, then cancel before installation.
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class IntroWizardSmoke {
    delegate bool Visitor(IntPtr window,IntPtr state);
    [DllImport("user32.dll")] static extern bool EnumWindows(Visitor visitor,IntPtr state);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent,Visitor visitor,IntPtr state);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out int process);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window,StringBuilder text,int count);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,StringBuilder text,int count);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window,int command);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] struct Rect {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr window,IntPtr dc,uint flags);
    public static IntPtr Main(int[] processes) {
        IntPtr result=IntPtr.Zero;
        EnumWindows((window,state)=> {GetWindowThreadProcessId(window,out int process);var name=new StringBuilder(128);GetClassName(window,name,128);if(Array.IndexOf(processes,process)>=0&&name.ToString()=="TWizardForm"){if(!IsWindowVisible(window))ShowWindow(window,4);result=window;return false;}return true;},IntPtr.Zero);return result;
    }
    public static IntPtr Button(int[] processes,string caption) {
        Main(processes);
        IntPtr result=IntPtr.Zero;
        EnumWindows((window,state)=> {
            GetWindowThreadProcessId(window,out int process);if(Array.IndexOf(processes,process)<0)return true;
            EnumChildWindows(window,(child,data)=> {var text=new StringBuilder(256);GetWindowText(child,text,256);if(IsWindowVisible(child)&&text.ToString().Replace("&","").Trim()==caption){result=child;return false;}return true;},IntPtr.Zero);
            return result==IntPtr.Zero;
        },IntPtr.Zero);return result;
    }
    public static void Click(IntPtr button) {SendMessage(button,0xF5,IntPtr.Zero,IntPtr.Zero);}
    public static void Close(int[] processes) {var window=Main(processes);if(window!=IntPtr.Zero)PostMessage(window,0x10,IntPtr.Zero,IntPtr.Zero);}
    public static int[] Bounds(IntPtr window) {if(!GetWindowRect(window,out var rect))throw new Exception("Intro window missing.");return new[]{rect.Right-rect.Left,rect.Bottom-rect.Top};}
    public static bool Print(IntPtr window,IntPtr dc) {return PrintWindow(window,dc,0);}
}
'@
    $introLog=Join-Path $PSScriptRoot 'artifacts/installer-intro.log'
    $introSetup=Start-Process -FilePath $setup -ArgumentList @('/SP-','/NORESTART',"/DIR=`"$installDir`"","/LOG=`"$introLog`"") -WindowStyle Hidden -PassThru
    function Intro-Processes {
        $ids=@($introSetup.Id)
        $processes=Get-CimInstance Win32_Process
        for($generation=0;$generation -lt 4;$generation++){$ids+=@($processes | Where-Object {$_.ParentProcessId -in $ids} | ForEach-Object {[int]$_.ProcessId})}
        return [int[]]@($ids | Select-Object -Unique)
    }
    function Capture-Intro([int[]]$ids,[string]$path) {
        $window=[IntroWizardSmoke]::Main($ids);$bounds=[IntroWizardSmoke]::Bounds($window)
        $bitmap=[System.Drawing.Bitmap]::new($bounds[0],$bounds[1])
        try{$graphics=[System.Drawing.Graphics]::FromImage($bitmap);try{$dc=$graphics.GetHdc();try{if(-not [IntroWizardSmoke]::Print($window,$dc)){throw 'Intro capture failed.'}}finally{$graphics.ReleaseHdc($dc)}}finally{$graphics.Dispose()};$bitmap.Save($path,[System.Drawing.Imaging.ImageFormat]::Png)}finally{$bitmap.Dispose()}
    }
    $introDeadline=[DateTime]::UtcNow.AddSeconds(20)
    do{$introIds=Intro-Processes;$introSkip=[IntroWizardSmoke]::Button($introIds,'Skip intro');if($introSkip -eq [IntPtr]::Zero){Start-Sleep -Milliseconds 250}}while($introSkip -eq [IntPtr]::Zero -and [DateTime]::UtcNow -lt $introDeadline)
    if($introSkip -eq [IntPtr]::Zero){throw 'The first-install intro and skip button did not appear.'}
    Capture-Intro $introIds (Join-Path $PSScriptRoot 'artifacts/previews/setup-intro-controls.png')
    $introNext=[IntroWizardSmoke]::Button($introIds,'Next >')
    if($introNext -eq [IntPtr]::Zero){throw 'The intro Next button is missing.'}
    [IntroWizardSmoke]::Click($introNext)
    Start-Sleep -Milliseconds 300
    Capture-Intro $introIds (Join-Path $PSScriptRoot 'artifacts/previews/setup-intro-settings.png')
    [IntroWizardSmoke]::Click([IntroWizardSmoke]::Button($introIds,'Skip intro'))
    [IntroWizardSmoke]::Close($introIds)
    $introDeadline=[DateTime]::UtcNow.AddSeconds(15)
    while(-not $introSetup.HasExited -and [DateTime]::UtcNow -lt $introDeadline){$introYes=[IntroWizardSmoke]::Button((Intro-Processes),'Yes');if($introYes -ne [IntPtr]::Zero){[IntroWizardSmoke]::Click($introYes)};Start-Sleep -Milliseconds 200}
    if(-not $introSetup.HasExited){throw 'The intro smoke check did not cancel Setup.'}
    $introTrace=Get-Content -LiteralPath $introLog -Raw
    foreach($expected in @('WaveWheels intro: first install','WaveWheels intro: controls page shown','WaveWheels intro: settings page shown','WaveWheels intro: skipped by user')){if(-not $introTrace.Contains($expected)){throw "Intro behavior missing: $expected"}}
    if((Test-Path -LiteralPath (Join-Path $installDir 'WaveWheels.exe')) -or (Test-Path -LiteralPath (Join-Path $profile 'settings.json'))){throw 'The intro smoke check changed app files or settings before installation.'}
    Write-Output 'PASS: first-install controls/settings pages, screenshots, Next, Skip intro, and cancellation before installation.'
    New-Item -ItemType Directory -Path $profile -Force | Out-Null
    $settingsFile = Join-Path $profile 'settings.json'
    $fixture = @{ SchemaVersion=1;EdgeEnabled=$false;CheckUpdatesAutomatically=$false;PortOverride=65535;WheelSizePercent=125;AnimateWheels=$true;ScrollStepPercent=3;ShiftStepPercent=35;ShowShiftMarkers=$true;ScrollAccelerationEnabled=$false;IdleMilliseconds=2500;Wheels=@(@{Id='installer-fixture';Name='Kept through upgrades';Color='#36BBD3';ChannelId='fake-input';MixId='';MaximumVolumePercent=65}) }
    $fixture | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $settingsFile
    $runKey = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Run'
    New-Item -Path $runKey -Force | Out-Null
    New-ItemProperty -Path $runKey -Name WaveWheels -Value '"C:\Old App\WaveWheels.exe"' -PropertyType String -Force | Out-Null
    function Run-Setup([string]$logName,[bool]$inAppUpdate=$false) {
        $log = Join-Path $PSScriptRoot ('artifacts/' + $logName)
        $arguments=@('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',"/DIR=`"$installDir`"","/LOG=`"$log`"")
        if($inAppUpdate) {$arguments+='/UPDATEFROMAPP=1'}
        $process = Start-Process -FilePath $setup -ArgumentList $arguments -WindowStyle Hidden -PassThru
        if (-not $process.WaitForExit(120000)) { throw 'Setup timed out.' }
        if ($process.ExitCode -ne 0) { throw "Setup failed with exit code $($process.ExitCode)." }
    }
    function Assert-Settings {
        $saved = Get-Content -LiteralPath $settingsFile -Raw | ConvertFrom-Json
        if ($saved.Wheels[0].Id -ne 'installer-fixture' -or $saved.Wheels[0].Name -ne 'Kept through upgrades' -or $saved.WheelSizePercent -ne 125) { throw 'Installation did not preserve saved wheel preferences.' }
        if ($saved.ScrollStepPercent -ne 3 -or $saved.ScrollAccelerationEnabled -or $saved.IdleMilliseconds -ne 2500 -or $saved.Wheels[0].MaximumVolumePercent -ne 65) {throw 'Update did not retain tuning and volume limits.'}
        if($saved.ShiftStepPercent -ne 35 -or -not $saved.ShowShiftMarkers){throw 'Update did not retain Shift increment and dot visibility.'}
    }
    Run-Setup 'installer-install.log'
    $installTrace=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'artifacts/installer-install.log') -Raw
    if(-not $installTrace.Contains('WaveWheels intro: existing install or settings') -or $installTrace.Contains('WaveWheels intro: controls page shown')){throw 'Existing settings did not bypass onboarding.'}
    $exe = Join-Path $installDir 'WaveWheels.exe'
    foreach ($name in @('WaveWheels.exe','hostfxr.dll','coreclr.dll','PresentationFramework.dll','LICENSE','NOTICE','unins000.exe')) {
        if (-not (Test-Path -LiteralPath (Join-Path $installDir $name))) { throw "Installer payload missing $name." }
    }
    $shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'WaveWheels/Configure WaveWheels.lnk'
    if (-not (Test-Path -LiteralPath $shortcut)) { throw 'The configuration Start menu shortcut is missing.' }
    if ((Get-ItemPropertyValue -Path $runKey -Name WaveWheels) -ne ('"' + $exe + '"')) { throw 'Upgrade did not update the existing startup registration.' }
    Assert-Settings
    $app = Start-Process -FilePath $exe -WindowStyle Hidden -PassThru
    if ($app.WaitForExit(2000)) { throw 'The installed self-contained app exited unexpectedly.' }
    $app.Refresh()
    if ($app.MainWindowHandle -ne 0) { throw 'Normal launch unexpectedly opened configuration.' }
    Run-Setup 'installer-upgrade.log' $true
    $upgradeTrace=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'artifacts/installer-upgrade.log') -Raw
    if($upgradeTrace.Contains('WaveWheels intro: controls page shown') -or $upgradeTrace.Contains('WaveWheels intro: settings page shown')){throw 'An in-app upgrade unexpectedly showed onboarding.'}
    if (-not $app.WaitForExit(10000)) { throw 'Upgrade did not close the running app gracefully.' }
    Assert-Settings
    $deadline=[DateTime]::UtcNow.AddSeconds(20)
    $reopened=$null
    do {
        $reopened=Get-Process -Name WaveWheels -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $exe -and $_.Id -ne $app.Id -and $_.MainWindowHandle -ne 0} | Select-Object -First 1
        if(-not $reopened){Start-Sleep -Milliseconds 250}
    } while(-not $reopened -and [DateTime]::UtcNow -lt $deadline)
    if(-not $reopened){throw 'In-app upgrade did not automatically reopen configuration.'}
    $app=$reopened
    $uninstaller = Start-Process -FilePath (Join-Path $installDir 'unins000.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -WindowStyle Hidden -PassThru
    if (-not $uninstaller.WaitForExit(120000) -or $uninstaller.ExitCode -ne 0) { throw 'Uninstall failed.' }
    if (-not $app.WaitForExit(10000)) { throw 'Uninstall left the app running.' }
    if (Test-Path -LiteralPath $exe) { throw 'Uninstall left the executable behind.' }
    if (Get-ItemProperty -Path $runKey -Name WaveWheels -ErrorAction SilentlyContinue) { throw 'Uninstall left its startup entry behind.' }
    Assert-Settings
    Write-Output 'PASS: install, bundled runtime, shortcuts, quiet launch, graceful in-app upgrade, automatic configuration reopening, startup path, uninstall, and retained settings/tuning/limits.'
}
finally { Pop-Location }
