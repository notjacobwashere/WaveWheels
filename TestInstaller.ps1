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
    $fixture = @{ SchemaVersion=1;EdgeEnabled=$false;PortOverride=65535;WheelSizePercent=125;AnimateWheels=$true;Wheels=@(@{Id='installer-fixture';Name='Kept through upgrades';Color='#36BBD3';ChannelId='fake-input';MixId=''}) }
    $fixture | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $settingsFile
    $runKey = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Run'
    New-Item -Path $runKey -Force | Out-Null
    New-ItemProperty -Path $runKey -Name WaveWheels -Value '"C:\Old App\WaveWheels.exe"' -PropertyType String -Force | Out-Null
    function Run-Setup([string]$logName) {
        $log = Join-Path $PSScriptRoot ('artifacts/' + $logName)
        $process = Start-Process -FilePath $setup -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',"/DIR=`"$installDir`"","/LOG=`"$log`"") -WindowStyle Hidden -PassThru
        if (-not $process.WaitForExit(120000)) { throw 'Setup timed out.' }
        if ($process.ExitCode -ne 0) { throw "Setup failed with exit code $($process.ExitCode)." }
    }
    function Assert-Settings {
        $saved = Get-Content -LiteralPath $settingsFile -Raw | ConvertFrom-Json
        if ($saved.Wheels[0].Id -ne 'installer-fixture' -or $saved.Wheels[0].Name -ne 'Kept through upgrades' -or $saved.WheelSizePercent -ne 125) { throw 'Installation did not preserve saved wheel preferences.' }
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
    Run-Setup 'installer-upgrade.log'
    if (-not $app.WaitForExit(10000)) { throw 'Upgrade did not close the running app gracefully.' }
    Assert-Settings
    $app = Start-Process -FilePath $exe -WindowStyle Hidden -PassThru
    if ($app.WaitForExit(1500)) { throw 'The upgraded app exited unexpectedly.' }
    $uninstaller = Start-Process -FilePath (Join-Path $installDir 'unins000.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -WindowStyle Hidden -PassThru
    if (-not $uninstaller.WaitForExit(120000) -or $uninstaller.ExitCode -ne 0) { throw 'Uninstall failed.' }
    if (-not $app.WaitForExit(10000)) { throw 'Uninstall left the app running.' }
    if (Test-Path -LiteralPath $exe) { throw 'Uninstall left the executable behind.' }
    if (Get-ItemProperty -Path $runKey -Name WaveWheels -ErrorAction SilentlyContinue) { throw 'Uninstall left its startup entry behind.' }
    Assert-Settings
    Write-Output 'PASS: install, bundled runtime, shortcuts, quiet launch, graceful upgrade, startup path, uninstall, and retained settings.'
}
finally { Pop-Location }
