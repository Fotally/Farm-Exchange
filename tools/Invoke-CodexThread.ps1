param(
    [Parameter(Mandatory)][ValidateSet('Doctor', 'Create', 'Send', 'Read', 'Wait', 'Reconcile', 'InterruptTest', 'DeleteTest')][string]$Action,
    [string]$RegistryPath,
    [string]$Worktree,
    [string]$Branch,
    [string]$Name,
    [string]$ThreadId,
    [string]$MessageFile,
    [ValidateSet('read-only', 'workspace-write', 'danger-full-access')][string]$Sandbox = 'workspace-write',
    [switch]$TestThread,
    [ValidateRange(0, 50)][int]$WaitSeconds = 30,
    [string]$SocketPath = (Join-Path $env:USERPROFILE '.codex/app-server-control/app-server-control.sock')
)

$ErrorActionPreference = 'Stop'

# 用本地 socket 连接现有共享服务器；不启动替代会话进程或修改全局配置。
if (-not ('FarmExchange.CodexThreadRpc' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FarmExchange {
    public sealed class CodexThreadRpc : IDisposable {
        private readonly ClientWebSocket socket = new ClientWebSocket();
        private readonly HttpMessageInvoker http;
        public CodexThreadRpc(string path) {
            var handler = new SocketsHttpHandler();
            handler.UseProxy = false;
            handler.ConnectCallback = async (context, token) => {
                var local = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try {
                    await local.ConnectAsync(new UnixDomainSocketEndPoint(path), token);
                    return new NetworkStream(local, ownsSocket: true);
                } catch { local.Dispose(); throw; }
            };
            http = new HttpMessageInvoker(handler);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            socket.ConnectAsync(new Uri("ws://localhost/"), http, timeout.Token).GetAwaiter().GetResult();
        }
        public void Send(string text) {
            var bytes = Encoding.UTF8.GetBytes(text);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, timeout.Token).GetAwaiter().GetResult();
        }
        public string Request(string text, int id) {
            Send(text);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (true) {
                using var buffer = new MemoryStream();
                var chunk = new byte[8192];
                WebSocketReceiveResult received;
                do {
                    received = socket.ReceiveAsync(new ArraySegment<byte>(chunk), timeout.Token).GetAwaiter().GetResult();
                    if (received.MessageType == WebSocketMessageType.Close)
                        throw new IOException("Codex App Server 关闭了连接。");
                    buffer.Write(chunk, 0, received.Count);
                } while (!received.EndOfMessage);
                var message = Encoding.UTF8.GetString(buffer.ToArray());
                using var json = JsonDocument.Parse(message);
                var root = json.RootElement;
                if (!root.TryGetProperty("id", out var requestId)) continue;
                if (root.TryGetProperty("method", out _)) {
                    // 不自动批准服务器请求；调用方可在 Read 中检查等待状态。
                    Send("{\"id\":" + requestId.GetRawText() + ",\"error\":{\"code\":-32601,\"message\":\"此客户端不自动处理交互请求\"}}");
                    continue;
                }
                if (requestId.ValueKind == JsonValueKind.Number && requestId.GetInt32() == id)
                    return message;
            }
        }
        public void Dispose() { socket.Dispose(); http.Dispose(); }
    }
}
'@
}

function Invoke-ThreadRpc([string]$Method, [hashtable]$Parameters) {
    $script:requestNumber++
    $request = @{ id = $script:requestNumber; method = $Method; params = $Parameters } | ConvertTo-Json -Depth 40 -Compress
    $response = $script:rpc.Request($request, $script:requestNumber) | ConvertFrom-Json -AsHashtable
    if ($response.ContainsKey('error')) {
        $rejection = [InvalidOperationException]::new("$Method : $($response.error | ConvertTo-Json -Compress -Depth 10)")
        $rejection.Data['CodexRpcRejected'] = $true
        throw $rejection
    }
    return $response.result
}

function Save-ThreadRegistry {
    $temporary = "$script:registryFile.tmp"
    [IO.File]::WriteAllText($temporary, ($script:registry | ConvertTo-Json -Depth 40), [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($temporary, $script:registryFile, $true)
}

function Assert-ThreadBranch([hashtable]$Entry) {
    $actual = (& git -C $Entry.worktree branch --show-current)
    if ($LASTEXITCODE -ne 0 -or $actual -ne $Entry.branch) {
        throw "工作区分支不匹配：$($Entry.worktree)；要求 $($Entry.branch)，实际 $actual。"
    }
}

function Get-ThreadSnapshot([hashtable]$Entry) {
    $metadata = Invoke-ThreadRpc 'thread/resume' @{ threadId = $Entry.threadId; excludeTurns = $true }
    if ([IO.Path]::GetFullPath($metadata.thread.cwd) -ne $Entry.worktree) {
        throw "服务器 cwd 与登记不符：$($Entry.threadId)"
    }
    $turns = Invoke-ThreadRpc 'thread/turns/list' @{ threadId = $Entry.threadId; limit = 20; itemsView = 'full'; sortDirection = 'desc' }
    $latest = @($turns.data | Where-Object id -eq $Entry.turnId) | Select-Object -First 1
    $messages = @($latest.items | Where-Object type -eq 'agentMessage' | ForEach-Object { $_.text })
    $terminals = Invoke-ThreadRpc 'thread/backgroundTerminals/list' @{ threadId = $Entry.threadId }
    return @{
        threadId = $Entry.threadId; worktree = $Entry.worktree; branch = $Entry.branch
        runtimeStatus = $metadata.thread.status; turnId = $Entry.turnId
        turnStatus = $(if ($Entry.state -eq 'sending') { 'sendUnconfirmed' } elseif ($null -eq $latest) { 'notStarted' } else { $latest.status })
        activeTerminals = $terminals.data; registryState = $Entry.state
        error = $latest.error; messages = $messages; turns = $turns.data
    }
}

function Get-ServerThreads([string]$Directory) {
    foreach ($archived in @($false, $true)) {
        $cursor = $null
        do {
            $parameters = @{ cwd = $Directory; archived = $archived; limit = 100; useStateDbOnly = $true }
            if ($cursor) { $parameters.cursor = $cursor }
            $page = Invoke-ThreadRpc 'thread/list' $parameters
            foreach ($thread in $page.data) { $thread }
            $cursor = $page.nextCursor
        } while ($cursor)
    }
}

function Get-LoadedThreadIds {
    $cursor = $null
    do {
        $parameters = @{ limit = 100 }
        if ($cursor) { $parameters.cursor = $cursor }
        $page = Invoke-ThreadRpc 'thread/loaded/list' $parameters
        foreach ($id in $page.data) { $id }
        $cursor = $page.nextCursor
    } while ($cursor)
}

function Assert-TestThreadDeleted([hashtable]$Entry) {
    foreach ($archived in @($false, $true)) {
        $cursor = $null
        do {
            $parameters = @{ cwd = $Entry.worktree; archived = $archived; limit = 100; useStateDbOnly = $true }
            if ($cursor) { $parameters.cursor = $cursor }
            $page = Invoke-ThreadRpc 'thread/list' $parameters
            if (@($page.data | Where-Object id -eq $Entry.threadId).Count -gt 0) { throw '永久删除后仍在会话列表中。' }
            $cursor = $page.nextCursor
        } while ($cursor)
    }
    $resumed = $false
    try { Invoke-ThreadRpc 'thread/resume' @{ threadId = $Entry.threadId; excludeTurns = $true } | Out-Null; $resumed = $true }
    catch { if ($_ -notmatch 'not found|no rollout|does not exist') { throw } }
    if ($resumed) { throw '永久删除后原 thread 仍能恢复。' }
}

$registryLock = $null
$rpc = $null
$requestNumber = 0
try {
    if ($Action -ne 'Doctor') {
        if (-not $RegistryPath) { throw '必须指定 RegistryPath，使用本轮独立登记文件。' }
        $registryFile = [IO.Path]::GetFullPath($RegistryPath)
        $directory = [IO.Path]::GetDirectoryName($registryFile)
        [IO.Directory]::CreateDirectory($directory) | Out-Null
        # 防止两个总控并发覆盖登记；Wait/Read 不持锁，不阻塞其他分支发送。
        if ($Action -in @('Create', 'Send', 'Reconcile', 'InterruptTest', 'DeleteTest')) {
            $registryLock = [IO.File]::Open("$registryFile.lock", 'OpenOrCreate', 'ReadWrite', 'None')
        }
        $registry = if (Test-Path -LiteralPath $registryFile) {
            Get-Content -LiteralPath $registryFile -Raw | ConvertFrom-Json -AsHashtable
        } else { @{ version = 1; entries = @() } }
        if ($registry.version -ne 1) { throw '不支持的会话登记版本。' }
        $entry = $null
        if ($Action -notin @('Create', 'Wait')) {
            if ($Action -eq 'Reconcile' -and $Worktree) {
                $resolvedWorktree = (Resolve-Path -LiteralPath $Worktree).Path
                $entry = @($registry.entries | Where-Object { $_.worktree -eq $resolvedWorktree -and -not $_.deleted }) | Select-Object -First 1
            } else {
                $entry = @($registry.entries | Where-Object { $ThreadId -and $_.threadId -eq $ThreadId }) | Select-Object -First 1
            }
            if ($null -eq $entry) { throw 'thread 未登记在本轮文件中，拒绝操作。' }
            if ($entry.deleted -and $Action -ne 'DeleteTest') { throw '该测试会话已经删除。' }
        }
    }

    $rpc = [FarmExchange.CodexThreadRpc]::new([IO.Path]::GetFullPath($SocketPath))
    $initialized = Invoke-ThreadRpc 'initialize' @{
        clientInfo = @{ name = 'farm_exchange_threads'; title = 'Farm Exchange 会话控制'; version = '1.0.0' }
        capabilities = @{ experimentalApi = $true }
    }
    $rpc.Send('{"method":"initialized","params":{}}')

    $result = switch ($Action) {
        'Doctor' { @{ connected = $true; transport = 'websocket-over-local-socket'; socketPath = $SocketPath; server = $initialized } }
        'Create' {
            if (-not $Worktree -or -not $Branch -or -not $Name) { throw 'Create 需要 Worktree、Branch 和 Name。' }
            if (-not $Branch.StartsWith('codex/')) { throw '仅接受 codex/ 修复分支。' }
            $worktreePath = (Resolve-Path -LiteralPath $Worktree).Path
            $entry = @{ worktree = $worktreePath; branch = $Branch; name = $Name; sandbox = $Sandbox; test = [bool]$TestThread; deleted = $false; threadId = $null; turnId = $null; state = 'creating' }
            Assert-ThreadBranch $entry
            if (@($registry.entries | Where-Object { -not $_.deleted -and $_.worktree -eq $worktreePath }).Count -gt 0) {
                throw '该 worktree 已登记；创建结果不明时先查询，禁止重复创建。'
            }
            $entry.existingThreadIds = @(Get-ServerThreads $worktreePath | ForEach-Object id)
            $entry.existingLoadedIds = @(Get-LoadedThreadIds)
            $entry.createStartedAt = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
            $entry.creationRequestId = 'farm_exchange_' + [Guid]::NewGuid().ToString('N')
            $previousEntries = @($registry.entries)
            $registry.entries = $previousEntries + @($entry)
            Save-ThreadRegistry
            $startParameters = @{
                cwd = $worktreePath; sandbox = $Sandbox; approvalPolicy = 'never'; ephemeral = $false; threadSource = $entry.creationRequestId
            }
            try { $started = Invoke-ThreadRpc 'thread/start' $startParameters }
            catch {
                if ($_.Exception.Data['CodexRpcRejected']) {
                    $registry.entries = $previousEntries
                    Save-ThreadRegistry
                }
                throw
            }
            $entry.threadId = $started.thread.id
            $entry.model = $started.model
            $entry.state = 'created'
            Save-ThreadRegistry
            if ([IO.Path]::GetFullPath($started.thread.cwd) -ne $worktreePath) { throw '创建后的服务器 cwd 不匹配。' }
            Invoke-ThreadRpc 'thread/name/set' @{ threadId = $entry.threadId; name = $Name } | Out-Null
            @{ threadId = $entry.threadId; worktree = $worktreePath; branch = $Branch; model = $entry.model; state = 'created' }
        }
        'Send' {
            Assert-ThreadBranch $entry
            if (-not $MessageFile) { throw 'Send 需要 UTF-8 MessageFile。' }
            if ($entry.state -eq 'sending') { throw '上次发送结果未确认；先 Reconcile 对账，不重复发送。' }
            $resumed = Invoke-ThreadRpc 'thread/resume' @{ threadId = $entry.threadId; excludeTurns = $true }
            if ([IO.Path]::GetFullPath($resumed.thread.cwd) -ne $entry.worktree) { throw '恢复后的 cwd 不匹配。' }
            if ($resumed.thread.status.type -eq 'active') { throw '原会话仍在运行，不能并发启动返修。' }
            $message = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $MessageFile).Path, [Text.Encoding]::UTF8)
            if ([string]::IsNullOrWhiteSpace($message)) { throw '消息不能为空。' }
            $previousEntry = $entry.Clone()
            $entry.state = 'sending'
            $entry.pendingMessageId = [Guid]::NewGuid().ToString()
            $entry.pendingMessagePrefix = "[Farm Exchange request: $($entry.pendingMessageId)]"
            Save-ThreadRegistry
            try {
                $turn = Invoke-ThreadRpc 'turn/start' @{ threadId = $entry.threadId; clientUserMessageId = $entry.pendingMessageId; input = @(@{ type = 'text'; text = "$($entry.pendingMessagePrefix)`n$message" }) }
            } catch {
                if ($_.Exception.Data['CodexRpcRejected']) {
                    $entry.Clear()
                    foreach ($key in $previousEntry.Keys) { $entry[$key] = $previousEntry[$key] }
                    Save-ThreadRegistry
                }
                throw
            }
            $entry.turnId = $turn.turn.id
            $entry.state = 'running'
            Save-ThreadRegistry
            @{ threadId = $entry.threadId; turnId = $entry.turnId; state = 'running' }
        }
        'Read' { Get-ThreadSnapshot $entry }
        'Reconcile' {
            Assert-ThreadBranch $entry
            if ($entry.state -eq 'creating') {
                if (-not $entry.createStartedAt -or -not $entry.creationRequestId) { throw '创建前基准或请求标记缺失，不能自动认领未知 thread。' }
                $known = @(Get-ServerThreads $entry.worktree)
                # 尚无首轮消息的 thread 可能只在内存中，需同时检查新加载的元数据。
                foreach ($id in @(Get-LoadedThreadIds)) {
                    if ($id -in $entry.existingLoadedIds -or $id -in @($known.id)) { continue }
                    $metadata = Invoke-ThreadRpc 'thread/read' @{ threadId = $id }
                    if ([IO.Path]::GetFullPath($metadata.thread.cwd) -eq $entry.worktree) { $known += $metadata.thread }
                }
                $candidates = @($known | Where-Object { $_.id -notin $entry.existingThreadIds -and $_.createdAt -ge $entry.createStartedAt -and $_.threadSource -ceq $entry.creationRequestId })
                if ($ThreadId) { $candidates = @($candidates | Where-Object id -eq $ThreadId) }
                if ($candidates.Count -ne 1) {
                    @{ confirmed = $false; state = 'creating'; candidateIds = @($candidates.id) }
                    break
                }
                $entry.threadId = $candidates[0].id
                $entry.state = 'created'
                Save-ThreadRegistry
                Invoke-ThreadRpc 'thread/name/set' @{ threadId = $entry.threadId; name = $entry.name } | Out-Null
            } elseif ($entry.state -eq 'sending') {
                if (-not $entry.pendingMessagePrefix) { throw '待确认消息缺少请求标记，不能自动对账。' }
                Invoke-ThreadRpc 'thread/resume' @{ threadId = $entry.threadId; excludeTurns = $true } | Out-Null
                $cursor = $null
                $matchedTurns = @()
                do {
                    $parameters = @{ threadId = $entry.threadId; limit = 100; itemsView = 'full'; sortDirection = 'desc' }
                    if ($cursor) { $parameters.cursor = $cursor }
                    $page = Invoke-ThreadRpc 'thread/turns/list' $parameters
                    foreach ($turn in $page.data) {
                        $found = @($turn.items | Where-Object {
                            $_.type -eq 'userMessage' -and @($_.content | Where-Object {
                                $_.type -eq 'text' -and $_.text.StartsWith($entry.pendingMessagePrefix + "`n", [StringComparison]::Ordinal)
                            }).Count -gt 0
                        })
                        if ($found.Count -gt 0) { $matchedTurns += $turn }
                    }
                    $cursor = $page.nextCursor
                } while ($cursor)
                if ($matchedTurns.Count -ne 1) {
                    @{ confirmed = $false; state = 'sending'; candidateTurnIds = @($matchedTurns.id) }
                    break
                }
                $entry.turnId = $matchedTurns[0].id
                $entry.state = 'running'
                Save-ThreadRegistry
            }
            @{ confirmed = $true; threadId = $entry.threadId; turnId = $entry.turnId; state = $entry.state }
        }
        'InterruptTest' {
            if (-not $entry.test) { throw '只允许中断本轮登记的测试会话。' }
            $snapshot = Get-ThreadSnapshot $entry
            $activeTurn = @($snapshot.turns | Where-Object status -eq 'inProgress') | Select-Object -First 1
            if ($null -ne $activeTurn) {
                Invoke-ThreadRpc 'turn/interrupt' @{ threadId = $entry.threadId; turnId = $activeTurn.id } | Out-Null
            }
            @{ threadId = $entry.threadId; interruptRequested = ($null -ne $activeTurn) }
        }
        'Wait' {
            $deadline = [DateTime]::UtcNow.AddSeconds($WaitSeconds)
            do {
                $registry = Get-Content -LiteralPath $registryFile -Raw | ConvertFrom-Json -AsHashtable
                $entries = @($registry.entries | Where-Object { -not $_.deleted -and (-not $ThreadId -or $_.threadId -eq $ThreadId) })
                if ($entries.Count -eq 0) { throw '没有匹配的登记会话。' }
                $snapshots = @($entries | ForEach-Object {
                    if ($_.threadId) { Get-ThreadSnapshot $_ }
                    else { @{ threadId = $null; worktree = $_.worktree; branch = $_.branch; turnStatus = 'createUnconfirmed' } }
                })
                $pending = @($snapshots | Where-Object { $_.turnStatus -notin @('completed', 'failed', 'interrupted') -or $_.runtimeStatus.type -eq 'active' -or @($_.activeTerminals).Count -gt 0 })
                if ($pending.Count -eq 0 -or [DateTime]::UtcNow -ge $deadline) { break }
                Start-Sleep -Milliseconds 500
            } while ($true)
            foreach ($snapshot in $snapshots) { $snapshot.Remove('turns') }
            @{ allCompleted = ($pending.Count -eq 0 -and @($snapshots | Where-Object turnStatus -ne 'completed').Count -eq 0); threads = $snapshots }
        }
        'DeleteTest' {
            if (-not $entry.test) { throw '只允许删除创建时明确登记为测试的 thread。' }
            if (-not $entry.deleted) {
                $snapshot = Get-ThreadSnapshot $entry
                if ($snapshot.runtimeStatus.type -eq 'active' -or @($snapshot.activeTerminals).Count -gt 0) { throw '测试会话仍在运行，停止后再删除。' }
                $entry.state = 'deleting'
                Save-ThreadRegistry
                Invoke-ThreadRpc 'thread/delete' @{ threadId = $entry.threadId } | Out-Null
            }
            Assert-TestThreadDeleted $entry
            $entry.deleted = $true
            $entry.state = 'deleted'
            Save-ThreadRegistry
            @{ threadId = $entry.threadId; deleted = $true; activeList = 'absent'; archivedList = 'absent'; resume = 'not-found' }
        }
    }
    $result | ConvertTo-Json -Depth 80 -Compress
}
finally {
    if ($null -ne $rpc) { $rpc.Dispose() }
    if ($null -ne $registryLock) { $registryLock.Dispose() }
}
