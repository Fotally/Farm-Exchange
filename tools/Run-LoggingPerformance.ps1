param(
    [Parameter(Mandatory = $true)][string]$GodotConsole,
    [string]$OutputDirectory = 'build/issue130-validation/performance',
    [string[]]$Cases = @()
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$outputRoot = [System.IO.Path]::GetFullPath((Join-Path $repository $OutputDirectory))
if (Test-Path -LiteralPath $outputRoot) {
    throw "输出目录已存在，请指定新的目录，避免混合证据：$outputRoot"
}
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$matrix = @()
foreach ($workload in @('full-1', 'full-16')) {
    foreach ($profile in @('off', 'runtime', 'development', 'failure')) {
        $matrix += [pscustomobject]@{ Workload = $workload; Profile = $profile }
    }
}
foreach ($profile in @('off', 'runtime', 'development')) {
    $matrix += [pscustomobject]@{ Workload = 'trades-multigame'; Profile = $profile }
}
$matrix += [pscustomobject]@{ Workload = 'bounded'; Profile = 'development' }
$results = @()
foreach ($case in $matrix) {
    $caseName = "$($case.Workload)-$($case.Profile)"
    if ($Cases.Count -gt 0 -and $caseName -notin $Cases) { continue }
    $caseDirectory = Join-Path $outputRoot $caseName
    New-Item -ItemType Directory -Path $caseDirectory | Out-Null
    Write-Host "日志矩阵 $caseName：1080P，预热 2 秒，采样 65 秒"
    # 每格新进程，Godot 自身打开真实图形测试窗口；不复用前一格的会话和缓存。
    & $GodotConsole --path $repository --resolution 1920x1080 tests/performance/test_logging_performance.tscn -- `
        --profile $case.Profile --workload $case.Workload --output $caseDirectory *> (Join-Path $caseDirectory 'process.log')
    $exitCode = $LASTEXITCODE
    $reportPath = Join-Path $caseDirectory 'report.json'
    if (Test-Path -LiteralPath $reportPath) {
        $result = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        $result | Add-Member -NotePropertyName ProcessExitCode -NotePropertyValue $exitCode
        $result | Add-Member -NotePropertyName EvidenceDirectory -NotePropertyValue $caseDirectory
        $results += $result
    }
    else {
        $results += [pscustomobject]@{ Workload = $case.Workload; Profile = $case.Profile; Passed = $false; ProcessExitCode = $exitCode; EvidenceDirectory = $caseDirectory }
    }
    # 中途失败也保存已取得的全部证据；继续其它独立格，最终统一返回失败。
    $results | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $outputRoot 'matrix.json') -Encoding utf8
}
if ($results.Count -eq 0) { throw '没有匹配的性能用例' }
$differences = foreach ($result in $results) {
    $baseline = $results | Where-Object { $_.Workload -eq $result.Workload -and $_.Profile -eq 'off' } | Select-Object -First 1
    if ($null -ne $baseline -and $null -ne $result.AverageFps -and $null -ne $baseline.AverageFps) {
        [pscustomobject]@{
            Workload = $result.Workload
            Profile = $result.Profile
            AverageFpsDelta = $result.AverageFps - $baseline.AverageFps
            P95FrameMsDelta = $result.P95FrameMs - $baseline.P95FrameMs
            ManagedAllocatedBytesPerSecondDelta = $result.ManagedAllocatedBytesPerSecond - $baseline.ManagedAllocatedBytesPerSecond
            ActualSimulationSecondsPerRealSecondDelta = $result.ActualSimulationSecondsPerRealSecond - $baseline.ActualSimulationSecondsPerRealSecond
        }
    }
}
$differences | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $outputRoot 'differences.json') -Encoding utf8
if (@($results | Where-Object { -not $_.Passed -or $_.ProcessExitCode -ne 0 }).Count -gt 0) {
    throw "日志图形矩阵未全部达到有效采样和 60 FPS/P95 16.67 ms 门槛，见 $outputRoot"
}
