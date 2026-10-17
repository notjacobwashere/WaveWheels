param()
$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath $PSScriptRoot
try {
    $assembly = Join-Path $PSScriptRoot 'App/WaveWheels.dll'
    if (-not (Test-Path -LiteralPath $assembly)) { throw 'Run Build.ps1 first.' }
    dotnet build 'Tests/WaveWheels.Tests.csproj' -c Release -p:UseSharedCompilation=false "-p:WaveWheelsAssembly=$assembly" -o 'artifacts/tests'
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    $tests = Join-Path $PSScriptRoot 'artifacts/tests/WaveWheels.Tests.dll'
    dotnet $tests --render 'artifacts/previews/studio.png'
    if ($LASTEXITCODE -ne 0) { throw 'WPF component tests failed.' }
    $modernProfile = Join-Path $PSScriptRoot 'artifacts/profiles/modern'
    node 'Tests/Run-ProtocolTests.cjs' "--assembly=$tests" "--profile=$modernProfile"
    if ($LASTEXITCODE -ne 0) { throw 'Modern Wave Link tests failed.' }
    $legacyProfile = Join-Path $PSScriptRoot 'artifacts/profiles/legacy'
    node 'Tests/Run-ProtocolTests.cjs' --legacy "--assembly=$tests" "--profile=$legacyProfile"
    if ($LASTEXITCODE -ne 0) { throw 'Legacy Wave Link tests failed.' }
}
finally { Pop-Location }
