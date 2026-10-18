param([string]$RuntimeVersion = '9.0.21')
$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath $PSScriptRoot
try {
    $compiler = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    $iscc = if ($compiler) { $compiler.Source } else { Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe' }
    if (-not (Test-Path -LiteralPath $iscc)) { throw 'Install Inno Setup 6 to build the installer. GitHub Windows runners include it.' }
    $pathMap = $PSScriptRoot + '=/_/WaveWheels'
    # Runtime packs are only needed for the installer; ordinary app builds stay package-free.
    dotnet publish 'Source/WaveWheels.csproj' -c Release -r win-x64 --self-contained true --source 'https://api.nuget.org/v3/index.json' "-p:RuntimeFrameworkVersion=$RuntimeVersion" -p:UseSharedCompilation=false -p:ContinuousIntegrationBuild=true -p:DebugType=embedded "-p:PathMap=$pathMap" -o 'artifacts/installer-app'
    if ($LASTEXITCODE -ne 0) { throw 'Self-contained app publish failed.' }
    [xml]$project = Get-Content -LiteralPath 'Source/WaveWheels.csproj' -Raw
    $version = [string]$project.Project.PropertyGroup.Version
    $numericVersion = (Get-Item -LiteralPath 'artifacts/installer-app/WaveWheels.exe').VersionInfo.FileVersion
    & $iscc "/DAppVersion=$version" "/DAppNumericVersion=$numericVersion" 'installer/WaveWheels.iss'
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    $setup = Join-Path $PSScriptRoot "artifacts/WaveWheels-$version-Setup.exe"
    $digest = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($setup + '.sha256', $digest + '  ' + [IO.Path]::GetFileName($setup) + [Environment]::NewLine)
    Write-Output $setup
}
finally { Pop-Location }
