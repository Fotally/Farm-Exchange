# Codex 独立会话控制

仓库使用 [Codex App Server 官方协议](https://learn.chatgpt.com/docs/app-server)控制持久化的独立 thread。主会话通过 [Invoke-CodexThread.ps1](../../tools/Invoke-CodexThread.ps1) 发出真实 `thread/start`、`thread/resume`、`turn/start`、历史查询和 `thread/delete` 请求；PowerShell 是协议客户端，不运行 `codex exec`，也不以子 Agent 代替分支会话。

## 接入条件

- Windows、PowerShell 7，以及已登录的 Codex。当前接入在 Codex 0.163.0 的本地共享 App Server 上验证；协议为实验接口，升级后重跑工具验收。
- 共享 App Server 已运行，默认 socket 为 `%USERPROFILE%\.codex\app-server-control\app-server-control.sock`。用 `codex app-server daemon version` 查询状态和实际 socket；自定义位置通过 `-SocketPath` 指定。工具不会重启现有服务、修改全局配置或读取/输出登录凭据。
- 若服务未运行，按本机 CLI 的 `codex app-server daemon start --help` 启动共享服务后再检查。工具只连接本地 socket，不开放 TCP 端口。
- 主会话无需安装新的 MCP 插件，可直接用命令工具调用此脚本。会话创建在共享服务器的持久存储中；桌面列表是否立即刷新由应用决定，验收以官方协议返回的真实 ID、历史和工作区为准。

```powershell
./tools/Invoke-CodexThread.ps1 -Action Doctor
```

## 创建、等待与原会话返修

先按 [多会话交付 skill](../../.codex/skills/farm-exchange-parallel-issue-delivery/SKILL.md) 分组并建立独立分支/worktree。每轮使用新的登记路径，例如 `build/issue-delivery/<轮次>/controller.json`；总控另在 `orchestration.json` 维护计划、预算、写入权及该登记文件路径。登记文件必须保留，不能手工把其他会话 ID 加进来。

```powershell
$threadRegistry = 'build/issue-delivery/<轮次>/controller.json'
./tools/Invoke-CodexThread.ps1 -Action Create -RegistryPath $threadRegistry `
    -Worktree '<已存在的worktree绝对路径>' -Branch 'codex/issue-<编号>-<主题>' `
    -Name 'Farm Exchange #<编号>'
```

`Create` 核对实际 Git 分支，创建独立 thread 并登记真实 ID，尚不启动模型。第一次 `Send` 仅发送 UTF-8 文件中的只读初始化提示；核对会话回报的真实目录、分支和基准后，第二次 `Send` 才派发完整工程统筹受派任务。

```powershell
./tools/Invoke-CodexThread.ps1 -Action Send -RegistryPath $threadRegistry `
    -ThreadId '<Create返回的threadId>' -MessageFile '<初始化提示文件>'
./tools/Invoke-CodexThread.ps1 -Action Wait -RegistryPath $threadRegistry -WaitSeconds 30
./tools/Invoke-CodexThread.ps1 -Action Read -RegistryPath $threadRegistry -ThreadId '<threadId>'
```

发送请求返回的是本次 `turnId`，不能当作工作完成。`Wait` 返回真实当前 turn 状态；超时仍返回当前快照，只有 `allCompleted=true` 才表示等待范围的当前 turn 全部成功结束。`failed`、`interrupted`、未开始或结果不明均不算成功；总控仍须检查代码、审查证据、干净提交和停止写入，不能凭 turn 完成替代交付验收。`Read` 提供最近 20 个 turn 的完整历史供核对，不代替 Git 差异审查。

需要返修时，以相同登记文件、相同 `threadId` 再执行 `Send`，消息文件包含当前 head、问题和验收条件。客户端先 `thread/resume`，然后在原 thread 开启新 turn；不新建 thread。运行中的原会话会拒绝并发返修。主会话退出命令客户端后，模型工作仍由共享服务器继续执行，后续命令可重新连接并读取。

创建或发送收到明确 RPC 拒绝时恢复此前登记状态，允许修正原因后重试；连接中断等结果不明的情况保留未确认状态，使用 `Reconcile` 对账，不重复创建或发送。创建前登记该工作区既有 ID、已加载 ID、请求时间与唯一请求标记，并将标记随 thread/start 写入服务器 threadSource；对账时查询持久会话和新加载会话的真实元数据，仅在请求标记、工作区及请求时间均匹配的新增候选唯一时恢复 thread ID；多个候选时返回清单，查证后用 `-ThreadId` 明确选择，不自行猜测。发送前保存唯一请求编号，同时作为 `clientUserMessageId` 和消息首行对账标记；对账分页检查原 thread 的持久化 userMessage 文本标记，恢复真正承载该消息的 turn ID。没有查证匹配结果时返回 `confirmed=false`，继续保持未确认状态；不能据列表暂时缺项推断请求未生效。

```powershell
./tools/Invoke-CodexThread.ps1 -Action Reconcile -RegistryPath $threadRegistry -Worktree '<原worktree绝对路径>'
./tools/Invoke-CodexThread.ps1 -Action Reconcile -RegistryPath $threadRegistry -ThreadId '<原threadId>'
```

登记锁用于阻止并发写入覆盖，锁文件可以保留；释放的是文件句柄，不以文件是否存在判断持锁。

## Windows shell 与权限

默认权限为 `workspace-write`、审批策略为 `never`；调用方可显式传入 `-Sandbox read-only`，或在当前任务已授权相同权限时传入 `-Sandbox danger-full-access`。实际权限记入登记与测试报告；工具不会在命令失败后自动扩大权限，命令失败照实交付。

本机受限沙箱曾因 WindowsApps PowerShell 启动报 `CreateProcessAsUserW failed: 5`；不能把这种无法核对分支的文字回复当作初始化通过，也不能写入未经版本 schema 支持的 shell 配置。当前主会话已授权完整访问的实测可显式传相同权限，但这不证明受限沙箱问题已解决；使用受限权限的正式任务仍须先解决本机执行环境问题。

## 真实接口测试与永久删除

[Test-CodexThreadControl.ps1](../../tools/Test-CodexThreadControl.ps1) 会运行真实模型请求。使用新的输出目录，创建一个小型测试仓库、两个独立修复分支/worktree 和两个独立 thread，执行只读初始化、分别写入 v1、原 thread 返修到 v2，并核对原上下文、实际分支命令和历史。

```powershell
./tools/Test-CodexThreadControl.ps1 -OutputDirectory 'build/codex-thread-control/<本轮测试>'
```

测试会话在创建时登记 `test=true`。测试的 `finally` 中结束仍在运行的测试 turn，再通过 `thread/delete` 永久删除；同时查证活动及归档列表均不含该 ID、原 ID 不能恢复。工作区和 JSON 证据保留在输出目录，便于追查。测试失败或清理失败均判为失败，查看 `report.json`，不得声称清理完成。

```powershell
./tools/Invoke-CodexThread.ps1 -Action DeleteTest -RegistryPath '<测试登记文件>' -ThreadId '<测试threadId>'
```

`DeleteTest` 只接受本轮登记且创建时标记为测试的会话，不删除正式分支会话，也不删除当前总控、源码、Git 分支或 worktree。正式任务结束后的分支清理和 issue 关闭继续遵循交付 skill；会话删除不代替这些操作。

## 已完成的实测

关联 issue #198。在本机 Codex 0.163.0、与主会话一致且已授权的 `danger-full-access` 权限下，双 thread 实测通过：两个独立分支分别完成初始化、v1 开发、原 thread 的 v2 返修，共六个真实 turn；保留原始暗号证明历史连续，真实 Git 命令确认工作区分支。错误分支、重复创建、未登记 ID、运行中重复发送及零秒等待的门槛也通过。

测试会话均通过 `thread/delete` 永久删除，并确认活动/归档列表缺席且原 ID 无法恢复。此前受限沙箱探测失败的测试会话也已清理。没有使用子 Agent 替代开发会话；测试工作区和详细证据留在 `build/codex-thread-control/`，不提交到 Git。这项验收证明会话控制链路可用，不冒充游戏 issue 的完整构建、PR 或人工验收。
