# GitHub Actions 持续集成

工作流检出时启用文件符号链接，并在测试前按 `.codex/rule-links.json` 修复与检查规则链接、文档路径和脚本命名空间，运行 `tools/Test-ValidationArtifacts.ps1` 的产物契约，再运行 `dotnet format whitespace FarmExchange.sln --verify-no-changes`。产物契约通过真实可执行夹具验证日志保全、干净分发 ZIP、启动非零及 60 秒超时，也检查 FPS 成功、失败、部分/零产物和实际工作流条件；它不替代正式导出启动或远端 Actions 实际上传。静态检查范围见[CI 静态检查](../static-checks/ci.md)；链接映射及 Windows 修复步骤见[规则加载](../../.codex/rule-loading.md)。

`.github/workflows/ci.yml` 使用 Windows 托管运行器、由根目录 [global.json](../../global.json) 固定的 .NET SDK 10.0.401 和 Godot 4.7.2 Mono，并按以下规则触发：

| 触发方式与目标 | 编译与 headless 场景测试 | 80% 行覆盖率门槛 | 图形 FPS 性能测试 | Windows Release 导出与启动 | 上传产物 |
| --- | --- | --- | --- | --- | --- |
| Pull Request 目标为 `dev` | 是 | 是 | 否 | 否 | Cobertura 报告，保留 14 天 |
| 推送到 `dev` | 是 | 是 | 否 | 否 | Cobertura 报告，保留 14 天 |
| 推送到 `main`（包括人工合并 PR） | 是 | 是 | 否 | 是 | 覆盖率、干净完整 Windows x86_64 目录及独立启动证据，保留 14 天 |
| 手动 `workflow_dispatch` 并勾选 `performance` | 是 | 是 | 是 | 仅在 `main` 执行 | 覆盖率、实际 FPS JSON/前后截图及清单；`main` 另含 Windows 产物，保留 14 天 |

目标为 `dev` 的 PR 门禁用于用户明确授权的分支整合。日常开发仍在 `dev`，正式交付通过 `dev` → `main` PR，由人工合并，遵循[GitHub 协作流程](contribution-workflow.md)。此类整合 PR 的 Windows CI 在合并前运行规则/格式检查、原生 PowerShell 产物契约、完整 headless 场景测试和覆盖率门禁；按 `pull_request` 默认检出待合并结果。PR 不自动执行图形 FPS 测量，也不导出正式 Windows 包，工作流的 Windows Release 导出仍仅在 `main` 运行。现有并发取消组继续按 `github.ref` 区分，后续更新同一 PR 会取消旧运行。

Windows 与 macOS 工作流都通过 `actions/setup-dotnet` 的 `global-json-file` 读取同一 SDK 版本；`global.json` 禁用版本前滚与预览版，仓库内的编译、格式检查和工具命令必须选择该确切 SDK，缺少时明确失败。项目目标框架为 `net10.0`；双平台 CI 除固定的 SDK 10.0.401 外暂保留 `8.0.x` 安装，其中 Windows 覆盖率工具 `dotnet-coverage` 仍以 `net8.0` 发布并使用 .NET 8 运行时。安装步骤后续均输出 `dotnet --version` 作为实际版本证据。

Godot、`dotnet-coverage` 和 GitHub 官方 Action 固定到明确主版本或工具版本。升级 SDK 时修改唯一约束 `global.json` 并同步安装说明；升级这些依赖均应通过独立 issue，在 `dev` 验证成功后再合并。`main` 的导出程序由 `Invoke-WindowsDistribution.ps1` 按真实进程退出码验收，成功清理分发输入后才上传 Windows 构建；产物名 `FarmExchange-windows-x86_64-<sha>` 包含提交 SHA，下载后应保持目录结构完整。它是提交级验收产物，不替代带版本号的正式 Release 压缩包。

Windows Release 导出成功后，共享分发命令复制该提交 schema 到 `logs/schema-v1.md`、真实启动并等待最多 60 秒，再将 `logs/runtime/`、`logs/debug/` 的现有会话及本次会话保存到 `build/validation/windows-<run-id>-<attempt>/`，逐文件校验成功后才移出构建目录。schema 字节与 EXE/PCK/程序集及其他依赖保留。启动成功或失败都通过显式 `!cancelled()` 条件上传 `windows-validation-<sha>-<run-id>-<attempt>`，包含 stdout、stderr、`startup.json` 和实际日志；失败结论继续保留，保存失败不继续上传分发包。玩家首次启动仍按原规则生成 exe 同级 runtime 日志。本地同一命令见[Windows 构建与导出](windows-build.md)。

## FPS 测量与失败证据

普通场景与覆盖率步骤完成后，仅手动选择 `performance=true` 才执行独立 `fps` 步骤，调用 `tools/Run-PerformanceTests.ps1 -GodotConsole`。本地 `Run-Tests.ps1 -Performance` 在原覆盖率门禁之后调用相同测量命令；测试前清除固定的三项旧报告/截图，沿用原有 60 FPS/P95 门槛、场景和失败退出码。

测量步骤结果为 `success` 或 `failure` 时，`Save-PerformanceEvidence.ps1` 复制本次实际存在的 `performance.json`、`performance.png`、`performance-end.png`，生成 `manifest.json` 记录每项存在/缺失、字节数、SHA256 及测量结果；清单不是测量报告，不填假 FPS。上传 `performance-<sha>-<run-id>-<attempt>`，保留 14 天。

| 情况 | 证据与结果 |
| --- | --- |
| 测量成功 | 上传实际 JSON、前后截图及清单 |
| 报告落盘后阈值失败 | 同样上传现有文件及清单，job 仍失败 |
| 测量已开始但仅部分或零产物 | 上传已有文件及缺失清单，不生成假 JSON，保留原错误 |
| 前置失败，`fps` skipped | 不收集、不上传性能证据，保留前置错误 |
| `performance=false` | 保持普通流程，不测量或收集 |
| 取消 | `!cancelled()` 跳过后续收集与上传，不承诺取消时的证据 |
| 收集或上传失败 | 步骤与 job 失败，不伪装证据已保存 |

收集与上传均显式使用 `!cancelled()`，排除隐含 `success()` 挡住阈值失败证据；上传还要求收集步骤成功。工作流不采用 `continue-on-error` 改变性能结论。

`.github/workflows/macos.yml` 是独立的 macOS 构建检查，在 `dev`、`main` 推送和手动触发时运行。它使用 macOS runner 编译 C#、导出 Universal 2 ZIP、检查双架构程序集并启动应用；通过后上传 ZIP，保留 14 天。详细命令、产物用途和签名范围见[macOS 构建与验收](macos-build.md)。

`.github/workflows/release.yml` 提供独立的「发布游戏版本」手动入口：选择 `main` 并输入版本号，精确检出触发 SHA 的分发命令与 schema，要求同提交的 Windows 主 CI 与 macOS CI 均成功，下载两平台产物；Windows 通过同一分发命令再次启动、外移新验收日志并检查最终 ZIP，macOS 检查 Universal 2 ZIP 后保留原包字节，两份 ZIP 一起上传到同一个 GitHub Release。任一平台无成功 CI、产物缺失或过期、包不完整都停止发布；无需本地上传，不重复编译，也不使用其他提交的产物。操作与失败处理见[正式版本发布](release.md)。
