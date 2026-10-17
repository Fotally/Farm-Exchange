param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateSet('read-only', 'workspace-write', 'danger-full-access')][string]$Sandbox = 'workspace-write',
    [ValidateRange(60, 900)][int]$StageTimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'
$controller = Join-Path $PSScriptRoot 'Invoke-CodexThread.ps1'
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw '使用尚不存在的测试输出目录，避免覆盖旧证据。' }
[IO.Directory]::CreateDirectory($output) | Out-Null
$registryFile = Join-Path $output 'threads.json'
$report = @{ passed = $false; stages = @(); cleanup = @(); output = $output; sandbox = $Sandbox }

function Invoke-Control([hashtable]$Arguments) {
    $json = & $controller -RegistryPath $registryFile @Arguments
    return $json | ConvertFrom-Json -AsHashtable
}

function Invoke-TestGit([string[]]$Arguments) {
    & git @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "测试 Git 命令失败：$($Arguments -join ' ')" }
}

function Send-TestMessage([hashtable]$Thread, [string]$Stage, [string]$Text, [bool]$LoseAcknowledgement = $false) {
    $file = Join-Path $output "$($Thread.label)-$Stage.txt"
    [IO.File]::WriteAllText($file, $Text, [Text.UTF8Encoding]::new($false))
    $response = Invoke-Control @{ Action = 'Send'; ThreadId = $Thread.threadId; MessageFile = $file }
    $report.stages += @{ label = $Thread.label; stage = $Stage; threadId = $response.threadId; turnId = $response.turnId }
    if ($LoseAcknowledgement) {
        # 服务端 turn 已生效，模拟客户端未将响应落盘；不重新提交模型请求。
        $lost = Get-Content -LiteralPath $registryFile -Raw | ConvertFrom-Json -AsHashtable
        $pending = @($lost.entries | Where-Object threadId -eq $Thread.threadId)[0]
        $pending.turnId = $Thread.turnId
        $pending.state = 'sending'
        [IO.File]::WriteAllText($registryFile, ($lost | ConvertTo-Json -Depth 40), [Text.UTF8Encoding]::new($false))
        $recovered = Invoke-Control @{ Action = 'Reconcile'; ThreadId = $Thread.threadId }
        $report.stages += @{ stage = 'send-reconciliation-result'; result = $recovered }
        if (-not $recovered.confirmed -or $recovered.turnId -ne $response.turnId) { throw '消息响应丢失后未恢复原 turn。' }
        $report.stages += @{ stage = 'reconcile-send'; threadId = $recovered.threadId; turnId = $recovered.turnId }
    }
    $Thread.turnId = $response.turnId
}

function Wait-TestStage {
    $limit = [DateTime]::UtcNow.AddSeconds($StageTimeoutSeconds)
    do {
        $state = Invoke-Control @{ Action = 'Wait'; WaitSeconds = 20 }
        foreach ($thread in $state.threads) {
            if ($thread.turnStatus -in @('failed', 'interrupted')) { throw "真实 turn 失败：$($thread | ConvertTo-Json -Compress -Depth 20)" }
        }
        if ($state.allCompleted) { return $state }
        if ([DateTime]::UtcNow -ge $limit) { throw '真实会话阶段等待超时。' }
        Write-Host '真实会话仍在执行；继续有界等待。'
    } while ($true)
}

function Assert-ControlRejected([hashtable]$Arguments, [string]$Pattern) {
    $rejected = $false
    try { Invoke-Control $Arguments | Out-Null }
    catch {
        if ($_.Exception.Message -notmatch $Pattern) { throw }
        $rejected = $true
        $report.stages += @{ stage = 'guard'; action = $Arguments.Action; rejection = $_.Exception.Message }
    }
    if (-not $rejected) { throw "预期拒绝的操作意外成功：$($Arguments.Action)" }
}

try {
    $report.connection = Invoke-Control @{ Action = 'Doctor' }
    $repository = Join-Path $output 'fixture'
    [IO.Directory]::CreateDirectory($repository) | Out-Null
    # 独立测试仓库，不读取或修改真实经营代码；工作区保留为验收证据。
    $rules = @'
# 会话控制接口测试夹具
这是已经授权的独立测试仓库；全部任务只操作本 worktree 的 result.txt。
不创建 GitHub issue/PR，不提交、不推送，不调用子 Agent，不启动后台进程，不访问其他工作区。
初始化时只读核对目录及分支；收到明确实施任务后才修改 result.txt。
用中文简短回报执行结果。上级 Farm Exchange 的游戏开发/导出流程不适用于此测试夹具。
'@
    [IO.File]::WriteAllText((Join-Path $repository 'AGENTS.md'), $rules, [Text.UTF8Encoding]::new($false))
    Invoke-TestGit -Arguments @('init', '-b', 'dev', $repository)
    Invoke-TestGit -Arguments @('-C', $repository, 'add', 'AGENTS.md')
    Invoke-TestGit -Arguments @('-C', $repository, '-c', 'user.name=Codex Thread Test', '-c', 'user.email=codex-thread-test@localhost', 'commit', '-m', 'test: isolated thread fixture')
    $threads = @()
    foreach ($label in @('a', 'b')) {
        $branchName = "codex/thread-control-$label"
        $tree = Join-Path $output "worktree-$label"
        Invoke-TestGit -Arguments @('-C', $repository, 'worktree', 'add', '-b', $branchName, $tree, 'dev')
        $created = Invoke-Control @{ Action = 'Create'; Worktree = $tree; Branch = $branchName; Name = "Farm Exchange 接口测试 $label $([IO.Path]::GetFileName($output))"; TestThread = $true; Sandbox = $Sandbox }
        $lost = Get-Content -LiteralPath $registryFile -Raw | ConvertFrom-Json -AsHashtable
        $pending = @($lost.entries | Where-Object threadId -eq $created.threadId)[0]
        $pending.threadId = $null
        $pending.state = 'creating'
        $creationRequestId = $pending.creationRequestId
        $pending.creationRequestId = 'farm_exchange_' + [Guid]::NewGuid().ToString('N')
        [IO.File]::WriteAllText($registryFile, ($lost | ConvertTo-Json -Depth 40), [Text.UTF8Encoding]::new($false))
        $unrelated = Invoke-Control @{ Action = 'Reconcile'; Worktree = $tree }
        if ($unrelated.confirmed) { throw '创建对账认领了请求身份不符的会话。' }
        $report.stages += @{ stage = 'reject-unrelated-create'; worktree = $tree }
        $pending.creationRequestId = $creationRequestId
        [IO.File]::WriteAllText($registryFile, ($lost | ConvertTo-Json -Depth 40), [Text.UTF8Encoding]::new($false))
        $recovered = Invoke-Control @{ Action = 'Reconcile'; Worktree = $tree }
        if (-not $recovered.confirmed -or $recovered.threadId -ne $created.threadId) { throw '创建响应丢失后未恢复原 thread。' }
        $report.stages += @{ stage = 'reconcile-create'; threadId = $recovered.threadId }
        $threads += @{ threadId = $created.threadId; worktree = $tree; branch = $branchName; label = $label; marker = [Guid]::NewGuid().ToString('N') }
    }
    if ($threads[0].threadId -eq $threads[1].threadId) { throw '两个独立会话返回了相同 thread ID。' }
    $report.threads = $threads
    Assert-ControlRejected @{ Action = 'Read'; ThreadId = [Guid]::NewGuid().ToString() } '未登记'
    Assert-ControlRejected @{ Action = 'Create'; Worktree = $threads[0].worktree; Branch = 'codex/wrong-branch'; Name = '不得创建' } '分支不匹配'
    Assert-ControlRejected @{ Action = 'Create'; Worktree = $threads[0].worktree; Branch = $threads[0].branch; Name = '不得重复创建' } '已登记'

    foreach ($thread in $threads) {
        Send-TestMessage $thread 'initialize' "仅只读核对当前工作区路径和 Git 分支，应为 $($thread.worktree) 和 $($thread.branch)。回报分支名；不得修改文件或运行验证，不用子 Agent。记住此会话独有暗号 $($thread.marker)，以后返修时需要回报它。核对完结束本轮回复，等待后续开发任务。"
        Assert-ControlRejected @{ Action = 'Send'; ThreadId = $thread.threadId; MessageFile = (Join-Path $output "$($thread.label)-initialize.txt") } '仍在运行'
    }
    $instant = Invoke-Control @{ Action = 'Wait'; WaitSeconds = 0 }
    if ($instant.allCompleted) { throw '运行中会话被零秒等待误报为完成。' }
    $initialized = Wait-TestStage
    $report.initialization = $initialized
    $report.initializationHistory = @()
    foreach ($thread in $threads) {
        $snapshot = @($initialized.threads | Where-Object threadId -eq $thread.threadId)[0]
        $history = Invoke-Control @{ Action = 'Read'; ThreadId = $thread.threadId }
        $report.initializationHistory += $history
        $turn = @($history.turns | Where-Object id -eq $history.turnId)[0]
        $proof = @($turn.items | Where-Object { $_.type -eq 'commandExecution' -and $_.exitCode -eq 0 -and $_.aggregatedOutput -match [regex]::Escape($thread.branch) })
        if ($proof.Count -eq 0) { throw '初始化缺少实际成功读取分支的命令证据。' }
        if (Test-Path -LiteralPath (Join-Path $thread.worktree 'result.txt')) { throw '只读初始化阶段提前写入。' }
    }
    $report.initialization = $initialized

    foreach ($thread in $threads) {
        Send-TestMessage $thread 'develop' "现在授权在本 worktree 开发测试夹具：创建 result.txt，内容严格为 v1-$($thread.label)，UTF-8。仅操作这个文件，不提交、不推送，不调用子 Agent。完成后回报文件内容，结束回复并停止写入。" $true
    }
    $report.development = Wait-TestStage
    foreach ($thread in $threads) {
        if ((Get-Content -LiteralPath (Join-Path $thread.worktree 'result.txt') -Raw).Trim() -ne "v1-$($thread.label)") { throw '独立开发产物不符。' }
    }

    foreach ($thread in $threads) {
        Send-TestMessage $thread 'repair' "返修本会话原任务：把 result.txt 改为 v2-$($thread.label)。回报修改结果和最早初始化时要求记住的此会话暗号；这条消息不再提供暗号。仅修改本文件，不提交、不推送，不用子 Agent，结束后停止写入。"
    }
    $repaired = Wait-TestStage
    foreach ($thread in $threads) {
        if ((Get-Content -LiteralPath (Join-Path $thread.worktree 'result.txt') -Raw).Trim() -ne "v2-$($thread.label)") { throw '返修产物不符。' }
        $snapshot = @($repaired.threads | Where-Object threadId -eq $thread.threadId)[0]
        if (($snapshot.messages -join '') -notmatch $thread.marker) { throw '原会话上下文未保留。' }
        $history = Invoke-Control @{ Action = 'Read'; ThreadId = $thread.threadId }
        if (@($history.turns).Count -lt 3) { throw '原会话不足三个真实 turn。' }
        if (@($history.turns.items | Where-Object type -like 'collab*').Count -gt 0) { throw '测试会话使用了子 Agent。' }
        $report.stages += @{ stage = 'history'; threadId = $thread.threadId; turnIds = @($history.turns.id) }
    }
    $report.repair = $repaired
    $report.passed = $true
}
catch {
    $report.failure = $_.Exception.Message
    throw
}
finally {
    if (Test-Path -LiteralPath $registryFile) {
        $registry = Get-Content -LiteralPath $registryFile -Raw | ConvertFrom-Json -AsHashtable
        foreach ($pending in @($registry.entries | Where-Object { $_.test -and -not $_.threadId -and $_.state -eq 'creating' })) {
            try {
                $recovered = Invoke-Control @{ Action = 'Reconcile'; Worktree = $pending.worktree }
                if (-not $recovered.confirmed) { throw '测试创建仍未确认，尚无法清理；需要继续对账。' }
            }
            catch { $report.passed = $false; $report.cleanup += @{ worktree = $pending.worktree; error = $_.Exception.Message } }
        }
        $registry = Get-Content -LiteralPath $registryFile -Raw | ConvertFrom-Json -AsHashtable
        foreach ($entry in @($registry.entries | Where-Object { $_.test -and $_.threadId -and -not $_.deleted })) {
            try {
                Invoke-Control @{ Action = 'InterruptTest'; ThreadId = $entry.threadId } | Out-Null
                Invoke-Control @{ Action = 'Wait'; ThreadId = $entry.threadId; WaitSeconds = 20 } | Out-Null
                $report.cleanup += Invoke-Control @{ Action = 'DeleteTest'; ThreadId = $entry.threadId }
            }
            catch { $report.passed = $false; $report.cleanup += @{ threadId = $entry.threadId; error = $_.Exception.Message } }
        }
    }
    [IO.File]::WriteAllText((Join-Path $output 'report.json'), ($report | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
    if (-not $report.passed) { Write-Error "测试或清理失败，查看 $output/report.json" }
}

Write-Output "真实会话创建、等待、原会话返修与永久删除测试通过：$output/report.json"
