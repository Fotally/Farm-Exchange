param(
    [Parameter(Mandatory = $true)]
    [string]$GodotConsole,
    [switch]$Performance
)

$ErrorActionPreference = 'Stop'
$env:NUGET_PACKAGES = Join-Path (Get-Location) '.nuget/ci-packages'
$env:DOTNET_COVERAGE_TELEMETRY_OPTOUT = '1'
$env:DOTNET_COVERAGE_NOLOGO = '1'

& (Join-Path $PSScriptRoot 'Test-CoverageGate.ps1')

dotnet tool restore
if ($LASTEXITCODE -ne 0) {
    throw "Coverage tool restore failed with exit code $LASTEXITCODE"
}

dotnet build FarmExchange.csproj --configuration Debug
if ($LASTEXITCODE -ne 0) {
    throw "Debug build failed with exit code $LASTEXITCODE"
}

& $GodotConsole --headless --path . --import
if ($LASTEXITCODE -ne 0) {
    throw "Godot asset import failed with exit code $LASTEXITCODE"
}

New-Item -ItemType Directory -Path coverage -Force | Out-Null
dotnet tool run dotnet-coverage collect `
    --include-files .godot/mono/temp/bin/Debug/FarmExchange.dll `
    --settings coverage.settings `
    --output coverage/coverage.cobertura.xml `
    --output-format cobertura `
    $GodotConsole --headless --path . tests/test_suite.tscn
if ($LASTEXITCODE -ne 0) {
    throw "Scene tests or coverage collection failed with exit code $LASTEXITCODE"
}

& (Join-Path $PSScriptRoot 'Test-Coverage.ps1') -ReportPath coverage/coverage.cobertura.xml | Out-Null

if ($Performance) {
    & (Join-Path $PSScriptRoot 'Run-PerformanceTests.ps1') -GodotConsole $GodotConsole
}
