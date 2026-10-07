param(
    [string]$ReportPath = 'coverage/coverage.cobertura.xml',
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$repositoryPath = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$scriptsPath = [IO.Path]::GetFullPath((Join-Path $repositoryPath 'scripts'))
$pathComparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
$pathComparer = if ($IsWindows) { [StringComparer]::OrdinalIgnoreCase } else { [StringComparer]::Ordinal }
$modules = [Collections.Generic.Dictionary[string, object]]::new($pathComparer)
foreach ($directory in Get-ChildItem -LiteralPath $scriptsPath -Directory) {
    $modules.Add($directory.Name, [Collections.Generic.Dictionary[string, bool]]::new($pathComparer))
}
if ($modules.Count -eq 0) {
    throw "没有可检查的业务模块：$scriptsPath"
}
if (-not (Test-Path -LiteralPath $ReportPath -PathType Leaf)) {
    throw "覆盖率报告不存在：$ReportPath"
}
[xml]$report = Get-Content -LiteralPath $ReportPath -Raw
if ($null -eq $report.SelectSingleNode('/coverage')) {
    throw "不是有效的 Cobertura 覆盖率报告：$ReportPath"
}

# 绝对路径直接定位；相对路径遵循报告 source，未提供 source 时相对仓库根目录。
$sourceRoots = @($report.SelectNodes('/coverage/sources/source') | ForEach-Object {
    [IO.Path]::GetFullPath($_.InnerText.Replace('\', '/'), $repositoryPath)
})
if ($sourceRoots.Count -eq 0) { $sourceRoots = @($repositoryPath) }
foreach ($class in $report.SelectNodes('/coverage/packages/package/classes/class')) {
    $filename = $class.GetAttribute('filename').Replace('\', '/')
    if ([string]::IsNullOrWhiteSpace($filename)) { throw "报告中的 class 缺少文件路径：$ReportPath" }
    $candidates = if ([IO.Path]::IsPathRooted($filename)) {
        @([IO.Path]::GetFullPath($filename))
    } else {
        @($sourceRoots | ForEach-Object { [IO.Path]::GetFullPath($filename, $_) })
    }
    $businessPaths = @($candidates | Where-Object {
        $_.StartsWith($scriptsPath + [IO.Path]::DirectorySeparatorChar, $pathComparison) -and
        (Test-Path -LiteralPath $_ -PathType Leaf)
    } | Select-Object -Unique)
    if ($businessPaths.Count -gt 1) { throw "覆盖率文件路径有歧义：$filename" }
    if ($businessPaths.Count -eq 0) { continue }
    $relativePath = [IO.Path]::GetRelativePath($scriptsPath, $businessPaths[0]).Replace('\', '/')
    $moduleName = $relativePath.Split('/')[0]
    if (-not $modules.ContainsKey($moduleName)) { throw "业务脚本不属于一级目录模块：$relativePath" }
    $lines = $modules[$moduleName]
    foreach ($line in $class.SelectNodes('lines/line')) {
        $lineNumber = 0
        $hits = 0L
        if (-not [int]::TryParse($line.GetAttribute('number'), [ref]$lineNumber) -or $lineNumber -le 0 -or
            -not [long]::TryParse($line.GetAttribute('hits'), [ref]$hits) -or $hits -lt 0) {
            throw "覆盖率行记录无效：$relativePath，行号 $($line.GetAttribute('number'))"
        }
        $key = "${relativePath}:$lineNumber"
        $lines[$key] = ($hits -gt 0) -or ($lines.ContainsKey($key) -and $lines[$key])
    }
}

$results = @()
foreach ($moduleName in ($modules.Keys | Sort-Object)) {
    $lines = $modules[$moduleName]
    $covered = @($lines.Values | Where-Object { $_ }).Count
    $results += [pscustomobject]@{ Module = $moduleName; Covered = $covered; Valid = $lines.Count }
}
$results += [pscustomobject]@{
    Module = '总体'
    Covered = ($results | Measure-Object -Property Covered -Sum).Sum
    Valid = ($results | Measure-Object -Property Valid -Sum).Sum
}
$failures = @()
foreach ($result in $results) {
    $percent = if ($result.Valid -eq 0) { 0 } else { 100.0 * $result.Covered / $result.Valid }
    $result | Add-Member -NotePropertyName Percent -NotePropertyValue $percent
    Write-Host ('{0}: {1}/{2} ({3:F2}%)' -f $result.Module, $result.Covered, $result.Valid, $percent)
    if ($result.Valid -eq 0) {
        $failures += "$($result.Module) 无有效业务行（模块缺失或报告为空）"
    } elseif ([decimal]$result.Covered * 100 -lt [decimal]$result.Valid * 80) {
        $failures += "$($result.Module) 低于 80%"
    }
}
if ($failures.Count -gt 0) {
    throw "覆盖率门禁失败：$($failures -join '；')。报告：$ReportPath"
}
$results
