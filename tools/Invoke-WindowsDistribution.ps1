param(
    [Parameter(Mandatory = $true)]
    [string]$BuildDirectory,
    [Parameter(Mandatory = $true)]
    [string]$SchemaPath,
    [Parameter(Mandatory = $true)]
    [string]$EvidenceDirectory,
    [string]$ArchivePath
)

$ErrorActionPreference = 'Stop'
$comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
$build = [IO.Path]::GetFullPath($BuildDirectory, (Get-Location).Path).TrimEnd([IO.Path]::DirectorySeparatorChar)
$schema = [IO.Path]::GetFullPath($SchemaPath, (Get-Location).Path)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory, (Get-Location).Path).TrimEnd([IO.Path]::DirectorySeparatorChar)
$archive = if ($ArchivePath) { [IO.Path]::GetFullPath($ArchivePath, (Get-Location).Path) } else { $null }

function Test-WithinDirectory {
    param([string]$Path, [string]$Directory)
    return $Path.Equals($Directory, $comparison) -or $Path.StartsWith($Directory + [IO.Path]::DirectorySeparatorChar, $comparison)
}

if (-not (Test-Path -LiteralPath $build -PathType Container)) { throw "构建目录不存在：$build" }
if (-not (Test-Path -LiteralPath $schema -PathType Leaf)) { throw "同提交日志 schema 不存在：$schema" }
if ((Test-WithinDirectory $evidence $build) -or (Test-WithinDirectory $build $evidence)) {
    throw '验收证据目录与构建目录不能相同或互相包含'
}
if (Test-WithinDirectory $schema $build) { throw 'schema 源必须来自构建目录之外的同提交检出' }
if ($archive -and ((Test-WithinDirectory $archive $build) -or (Test-WithinDirectory $archive $evidence))) {
    throw '最终 ZIP 必须位于构建目录与验收证据目录之外'
}
if (Test-Path -LiteralPath $evidence) { throw "验收证据目录已存在，须使用本次运行的独立目录：$evidence" }
if ($archive -and (Test-Path -LiteralPath $archive)) { throw "最终 ZIP 已存在，拒绝覆盖：$archive" }

$requiredFiles = @('FarmExchange.exe', 'FarmExchange.pck', 'data_FarmExchange_windows_x86_64/FarmExchange.dll', 'logs/schema-v1.md')
$schemaHash = (Get-FileHash -LiteralPath $schema -Algorithm SHA256).Hash

function Assert-DistributionContent {
    param([hashtable]$Files, [string]$ActualSchemaHash, [string]$Location)
    foreach ($name in $requiredFiles) {
        if (-not $Files.ContainsKey($name) -or $Files[$name] -le 0) {
            throw "分发内容缺少有效运行文件：$Location -> $name"
        }
    }
    if (@($Files.Keys | Where-Object { $_ -match '^logs/(runtime|debug)(/|$)' }).Count -gt 0) {
        throw "分发内容含验收会话：$Location"
    }
    if ($ActualSchemaHash -cne $schemaHash) { throw "分发 schema 字节与同提交源不一致：$Location" }
}

function Assert-BuildContent {
    $files = @{}
    foreach ($file in Get-ChildItem -LiteralPath $build -File -Recurse -Force) {
        $files[[IO.Path]::GetRelativePath($build, $file.FullName).Replace('\', '/')] = $file.Length
    }
    $schemaInBuild = Join-Path $build 'logs/schema-v1.md'
    $hash = if (Test-Path -LiteralPath $schemaInBuild -PathType Leaf) { (Get-FileHash -LiteralPath $schemaInBuild -Algorithm SHA256).Hash } else { '' }
    Assert-DistributionContent $files $hash $build
}

function Assert-ArchiveContent {
    param([string]$Path)
    $zip = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $files = @{}
        foreach ($entry in $zip.Entries) { $files[$entry.FullName.Replace('\', '/')] = $entry.Length }
        $entry = $zip.GetEntry('logs/schema-v1.md')
        $hash = ''
        if ($null -ne $entry) {
            $stream = $entry.Open()
            try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
            finally { $stream.Dispose() }
        }
        Assert-DistributionContent $files $hash $Path
    } finally { $zip.Dispose() }
}

New-Item -ItemType Directory -Path $evidence | Out-Null
$primaryFailure = $null
$preservationFailures = [Collections.Generic.List[string]]::new()
$process = $null
$started = $false
$timedOut = $false
$exitCode = $null
$processExited = $true
$savedLogs = [Collections.Generic.List[object]]::new()
try {
    foreach ($name in $requiredFiles | Where-Object { $_ -ne 'logs/schema-v1.md' }) {
        $path = Join-Path $build $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) {
            throw "构建缺少有效运行文件：$path"
        }
    }
    New-Item -ItemType Directory -Path (Join-Path $build 'logs') -Force | Out-Null
    Copy-Item -LiteralPath $schema -Destination (Join-Path $build 'logs/schema-v1.md') -Force
    & (Join-Path $PSScriptRoot 'Test-RuntimePack.ps1') -PackPath (Join-Path $build 'FarmExchange.pck') -ReportPath (Join-Path $evidence 'runtime-pack.json')
    $startArguments = @{
        FilePath = Join-Path $build 'FarmExchange.exe'
        ArgumentList = @('--headless', '--quit-after', '5')
        WorkingDirectory = $build
        RedirectStandardOutput = Join-Path $evidence 'stdout.log'
        RedirectStandardError = Join-Path $evidence 'stderr.log'
        PassThru = $true
    }
    if ($IsWindows) { $startArguments.WindowStyle = 'Hidden' }
    $process = Start-Process @startArguments
    $started = $true
    if (-not $process.WaitForExit(60000)) {
        $timedOut = $true
        throw '导出程序启动验收超过 60 秒'
    }
    # 无参等待确认重定向输出也已完成。
    $process.WaitForExit()
    $exitCode = $process.ExitCode
    if ($exitCode -ne 0) { throw "导出程序启动失败，退出码：$exitCode" }
} catch {
    $primaryFailure = $_.Exception.Message
} finally {
    if ($null -ne $process) {
        try {
            if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
            $processExited = $process.HasExited
            if ($processExited) { $exitCode = $process.ExitCode }
        } catch {
            $processExited = $false
            $preservationFailures.Add("无法确认启动进程退出，日志源数据保留：$($_.Exception.Message)")
        }
        $process.Dispose()
    }
    foreach ($kind in @('runtime', 'debug')) {
        $source = Join-Path $build "logs/$kind"
        $present = Test-Path -LiteralPath $source -PathType Container
        $fileCount = 0
        $saved = $false
        if ($present -and $processExited) {
            try {
                $files = @(Get-ChildItem -LiteralPath $source -File -Recurse -Force)
                $fileCount = $files.Count
                $destination = Join-Path $evidence "logs/$kind"
                New-Item -ItemType Directory -Path $destination -Force | Out-Null
                # 所有文件复制及逐文件校验成功后才移除源目录，失败时保留源证据。
                foreach ($file in $files) {
                    $target = Join-Path $destination ([IO.Path]::GetRelativePath($source, $file.FullName))
                    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
                    Copy-Item -LiteralPath $file.FullName -Destination $target
                    if ((Get-FileHash -LiteralPath $file.FullName).Hash -cne (Get-FileHash -LiteralPath $target).Hash) {
                        throw "日志保存校验失败：$($file.FullName) -> $target"
                    }
                }
                Remove-Item -LiteralPath $source -Recurse -Force
                $saved = $true
            } catch { $preservationFailures.Add("$kind 日志未完整保存，源数据保留：$($_.Exception.Message)") }
        }
        $savedLogs.Add([ordered]@{ Kind = $kind; DirectoryPresent = $present; FileCount = $fileCount; Saved = $saved })
    }
    try {
        [ordered]@{
            Started = $started
            ProcessExited = $processExited
            TimedOut = $timedOut
            ExitCode = $exitCode
            StartupFailure = $primaryFailure
            PreservationFailures = @($preservationFailures.ToArray())
            Logs = @($savedLogs.ToArray())
        } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'startup.json') -Encoding utf8
    } catch { $preservationFailures.Add("无法记录启动事实：$($_.Exception.Message)") }
}

if ($primaryFailure -or $preservationFailures.Count -gt 0) {
    throw (@($primaryFailure) + $preservationFailures.ToArray() | Where-Object { $_ }) -join '; '
}
Assert-BuildContent
& (Join-Path $PSScriptRoot 'Test-RuntimePack.ps1') -PackPath (Join-Path $build 'FarmExchange.pck') -ReportPath (Join-Path $evidence 'runtime-pack-final.json')
if ($archive) {
    $archiveDirectory = Split-Path -Parent $archive
    New-Item -ItemType Directory -Path $archiveDirectory -Force | Out-Null
    $temporaryArchive = Join-Path $archiveDirectory ('.distribution-' + [guid]::NewGuid().ToString('N') + '.zip')
    try {
        [IO.Compression.ZipFile]::CreateFromDirectory($build, $temporaryArchive)
        Assert-ArchiveContent $temporaryArchive
        & (Join-Path $PSScriptRoot 'Test-RuntimePack.ps1') -PackPath $temporaryArchive -ReportPath (Join-Path $evidence 'runtime-pack-archive.json')
        [IO.File]::Move($temporaryArchive, $archive)
    } finally {
        if (Test-Path -LiteralPath $temporaryArchive) { Remove-Item -LiteralPath $temporaryArchive -Force }
    }
}
Write-Host "Windows 分发准备成功：$build；验收证据：$evidence"
