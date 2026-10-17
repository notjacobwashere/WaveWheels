param()
$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath $PSScriptRoot
try {
    # Map compiler source paths so distributed debug information does not expose local paths.
    $pathMap = $PSScriptRoot + '=/_/WaveWheels'
    dotnet publish 'Source/WaveWheels.csproj' -c Release --self-contained false -p:UseSharedCompilation=false -p:ContinuousIntegrationBuild=true -p:DebugType=embedded "-p:PathMap=$pathMap" -o 'App'
    if ($LASTEXITCODE -ne 0) { throw 'WaveWheels publish failed.' }
    Write-Output 'Built App/WaveWheels.exe. Keep the App folder together.'
}
finally { Pop-Location }
