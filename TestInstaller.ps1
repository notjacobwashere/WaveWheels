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
