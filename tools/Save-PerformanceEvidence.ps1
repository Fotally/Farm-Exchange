param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDirectory,
    [Parameter(Mandatory = $true)]
    [string]$DestinationDirectory,
    [Parameter(Mandatory = $true)]
    [ValidateSet('success', 'failure')]
    [string]$MeasurementOutcome
)

$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath($SourceDirectory, (Get-Location).Path)
$destination = [IO.Path]::GetFullPath($DestinationDirectory, (Get-Location).Path)
if ($source.Equals($destination, [StringComparison]::OrdinalIgnoreCase)) {
    throw '性能证据输出目录必须与原报告目录不同'
}
if (Test-Path -LiteralPath $destination) {
    throw "性能证据目录已存在，须为本次测量提供独立目录：$destination"
}
New-Item -ItemType Directory -Path $destination | Out-Null
$files = foreach ($name in @('performance.json', 'performance.png', 'performance-end.png')) {
    $path = Join-Path $source $name
    $present = Test-Path -LiteralPath $path -PathType Leaf
    if ($present) {
        Copy-Item -LiteralPath $path -Destination (Join-Path $destination $name)
    }
    [ordered]@{
        Name = $name
        Present = $present
        Length = if ($present) { (Get-Item -LiteralPath $path).Length } else { $null }
        Sha256 = if ($present) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash } else { $null }
    }
}
[ordered]@{
    MeasurementOutcome = $MeasurementOutcome
    Files = @($files)
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $destination 'manifest.json') -Encoding utf8
Write-Host "已保存本次 FPS 证据：$destination；测量结果：$MeasurementOutcome"
