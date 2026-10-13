param(
    [Parameter(Mandatory = $true)]
    [string]$GodotConsole
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path coverage -Force | Out-Null
foreach ($name in @('performance.json', 'performance.png', 'performance-end.png')) {
    $path = Join-Path 'coverage' $name
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

& $GodotConsole --path . tests/performance/test_full_world_fps.tscn
$exitCode = $LASTEXITCODE
if ($exitCode -ne 0) {
    throw "满地图 FPS 测量失败，退出码：$exitCode；已生成的文件保留在 coverage"
}
$report = 'coverage/performance.json'
if (-not (Test-Path -LiteralPath $report -PathType Leaf)) {
    throw '满地图 FPS 测量退出成功但没有生成 coverage/performance.json'
}
$result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
Write-Host "满地图平均 FPS：$([Math]::Round($result.AverageFps, 1))；P95 帧间隔：$([Math]::Round($result.P95FrameMs, 2)) ms"
