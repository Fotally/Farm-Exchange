param(
    [Parameter(Mandatory = $true)][string]$GodotConsole,
    [string]$OutputDirectory = 'build/issue130-validation/lifecycle'
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$launcherPath = (Resolve-Path -LiteralPath $GodotConsole).Path
$enginePath = $launcherPath -replace '_console\.exe$', '.exe'
$outputRoot = [IO.Path]::GetFullPath((Join-Path $repository $OutputDirectory))
if (Test-Path -LiteralPath $outputRoot) { throw "输出目录已存在：$outputRoot" }
New-Item -ItemType Directory -Path $outputRoot | Out-Null
if (-not ('LoggingWindowMessages' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class LoggingWindowMessages {
    [DllImport("user32.dll", SetLastError=true)]
    public static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
}
'@
}

function Resolve-VerifiedEngine {
    param([int]$EngineId, [int]$LauncherId, [DateTime]$LaunchStarted)
    if ($EngineId -le 0) { throw '准备工件中的引擎 PID 无效' }
    $instance = Get-CimInstance Win32_Process -Filter "ProcessId = $EngineId"
    if ($null -eq $instance -or $instance.ExecutablePath -notin @($launcherPath, $enginePath) -or
        $instance.CreationDate.ToUniversalTime() -lt $LaunchStarted.AddSeconds(-1)) {
        throw "准备工件 PID $EngineId 不是本次启动的对应 Godot 引擎"
    }
    $ancestry = @($EngineId)
    $current = $instance
    while ([int]$current.ProcessId -ne $LauncherId) {
        $parentId = [int]$current.ParentProcessId
        if ($parentId -le 0 -or $parentId -in $ancestry -or $ancestry.Count -ge 16) {
            throw "引擎 PID $EngineId 不属于启动 PID $LauncherId 的进程树"
        }
        $ancestry += $parentId
        if ($parentId -eq $LauncherId) { break }
        $current = Get-CimInstance Win32_Process -Filter "ProcessId = $parentId"
        if ($null -eq $current -or $current.CreationDate.ToUniversalTime() -lt $LaunchStarted.AddSeconds(-1)) {
            throw "无法核实引擎 PID $EngineId 的完整启动进程树"
        }
    }
    $engineProcess = Get-Process -Id $EngineId
    # Process 对象持有实际进程句柄，后续关闭只操作这一已核实对象。
    $null = $engineProcess.Handle
    return [pscustomobject]@{ Process = $engineProcess; ExecutablePath = $instance.ExecutablePath; Ancestry = $ancestry }
}

$results = @()
foreach ($mode in @('quit', 'window', 'kill')) {
    $directory = Join-Path $outputRoot $mode
    New-Item -ItemType Directory -Path $directory | Out-Null
    $arguments = @('--path', ('"' + $repository + '"'), '--resolution', '1920x1080', 'tests/integration/test_logging_lifecycle.tscn', '--', '--mode', $mode, '--output', ('"' + $directory + '"'))
    $launchStarted = [DateTime]::UtcNow
    $process = Start-Process -FilePath $launcherPath -ArgumentList $arguments -WorkingDirectory $repository -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $directory 'stdout.log') -RedirectStandardError (Join-Path $directory 'stderr.log')
    $engine = $null
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(45)
        while (-not (Test-Path -LiteralPath (Join-Path $directory 'ready.json'))) {
            if ([DateTime]::UtcNow -gt $deadline) { throw "生命周期 $mode 未进入准备状态，见 $directory" }
            Start-Sleep -Milliseconds 100
        }
        $ready = Get-Content -LiteralPath (Join-Path $directory 'ready.json') -Raw | ConvertFrom-Json
        if ($ready.Mode -ne $mode) { throw '生命周期准备工件的模式不符' }
        $verified = Resolve-VerifiedEngine -EngineId $ready.EngineProcessId -LauncherId $process.Id -LaunchStarted $launchStarted
        $engine = $verified.Process
        $identity = [pscustomobject]@{ LauncherProcessId = $process.Id; EngineProcessId = $engine.Id; ExecutablePath = $verified.ExecutablePath; Ancestry = $verified.Ancestry; LaunchStartedUtc = $launchStarted }
        $identity | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $directory 'process-identity.json') -Encoding utf8
        Set-Content -LiteralPath (Join-Path $directory 'verified.txt') -Value $engine.Id -Encoding utf8
        if ($mode -eq 'kill') { Stop-Process -InputObject $engine -Force }
        elseif ($mode -eq 'window') {
            $engine.Refresh()
            if ($engine.MainWindowHandle -eq [IntPtr]::Zero -or -not [LoggingWindowMessages]::PostMessage($engine.MainWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)) {
                throw '无法向实际 Godot 窗口发送 WM_CLOSE'
            }
        }
        if (-not $engine.WaitForExit(30000)) { throw "生命周期 $mode 的实际引擎未退出" }
        $paths = @(Get-ChildItem -LiteralPath (Join-Path $directory 'logs/runtime') -Filter '*.log' -File)
        $lines = @($paths | ForEach-Object { Get-Content -LiteralPath $_.FullName })
        $games = @($lines | Where-Object { $_ -match ' EventName=GameEnded ' }).Count
        $sessions = @($lines | Where-Object { $_ -match ' EventName=SessionEnded ' }).Count
        if ($mode -eq 'kill') {
            if ($sessions -ne 0 -or $games -ne 1) { throw '强杀证据没有保留主局和会话退出缺口' }
        }
        elseif ($engine.ExitCode -ne 0 -or $sessions -ne 1 -or $games -ne 2) { throw "正常退出或重复关闭事件错误：$mode" }
        foreach ($path in $paths) {
            $stream = [IO.File]::Open($path.FullName, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
            $stream.Dispose()
        }
        $results += [pscustomobject]@{ Mode = $mode; ProcessIdentity = $identity; GameEndedCount = $games; SessionEndedCount = $sessions; ExitCode = $engine.ExitCode; FilesReleased = $true; EvidenceDirectory = $directory; Passed = $true }
        $results | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $outputRoot 'report.json') -Encoding utf8
    }
    finally {
        if ($null -ne $engine) {
            if (-not $engine.HasExited) { Stop-Process -InputObject $engine -Force }
            $engine.Dispose()
        }
        if (-not $process.HasExited) { Stop-Process -InputObject $process -Force }
        $process.Dispose()
    }
}
