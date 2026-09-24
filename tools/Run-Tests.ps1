param(
    [Parameter(Mandatory = $true)]
    [string]$GodotConsole,
    [switch]$Performance
)

$ErrorActionPreference = 'Stop'
$env:NUGET_PACKAGES = Join-Path (Get-Location) '.nuget/ci-packages'
$env:DOTNET_COVERAGE_TELEMETRY_OPTOUT = '1'
$env:DOTNET_COVERAGE_NOLOGO = '1'

dotnet tool restore
if ($LASTEXITCODE -ne 0) {
    throw "Coverage tool restore failed with exit code $LASTEXITCODE"
}

dotnet build FarmExchange.csproj --configuration Debug
if ($LASTEXITCODE -ne 0) {
    throw "Debug build failed with exit code $LASTEXITCODE"
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

[xml]$coverage = Get-Content -LiteralPath coverage/coverage.cobertura.xml -Raw
$lineRate = [double]::Parse(
    $coverage.coverage.'line-rate',
    [System.Globalization.CultureInfo]::InvariantCulture
)
$lineCoverage = $lineRate * 100
$roundedLineCoverage = [Math]::Round($lineCoverage, 2)
Write-Host "Business script line coverage: $roundedLineCoverage%"
if ($lineCoverage -lt 80) {
    throw "Business script line coverage $roundedLineCoverage% is below the 80% threshold"
}

if ($Performance) {
    $report = 'coverage/performance.json'
    Remove-Item -LiteralPath $report -ErrorAction SilentlyContinue
    & $GodotConsole --path . tests/stress/test_performance.tscn
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $report)) {
        throw "Graphics performance test failed with exit code $LASTEXITCODE"
    }
    $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    Write-Host "Full-world average FPS: $([Math]::Round($result.AverageFps, 1)); P95 frame time: $([Math]::Round($result.P95FrameMs, 2)) ms"
}
