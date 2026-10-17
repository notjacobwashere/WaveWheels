param()
$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath $PSScriptRoot
try {
    if (-not (Test-Path -LiteralPath 'App/WaveWheels.exe')) { throw 'Run Build.ps1 first.' }
    [xml]$project = Get-Content -LiteralPath 'Source/WaveWheels.csproj' -Raw
    $version = [string]$project.Project.PropertyGroup.Version
    $stage = Join-Path $PSScriptRoot ('artifacts/package-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path (Join-Path $stage 'App') -Force | Out-Null
    foreach ($name in @('WaveWheels.exe','WaveWheels.dll','WaveWheels.deps.json','WaveWheels.runtimeconfig.json')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('App/' + $name)) -Destination (Join-Path $stage 'App')
    }
    foreach ($name in @('README.md','LICENSE','NOTICE','CHANGELOG.md')) {
        if (Test-Path -LiteralPath $name) { Copy-Item -LiteralPath $name -Destination $stage }
    }
    Copy-Item -LiteralPath 'docs' -Destination $stage -Recurse
    $archive = Join-Path $PSScriptRoot ("artifacts/WaveWheels-$version-windows-x64.zip")
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -CompressionLevel Optimal -Force
    $digest = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksum = $digest + '  ' + [IO.Path]::GetFileName($archive)
    [IO.File]::WriteAllText($archive + '.sha256', $checksum + [Environment]::NewLine)
    Write-Output $archive
    Write-Output $checksum
}
finally { Pop-Location }
