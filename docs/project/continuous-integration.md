# GitHub Actions 持续集成

工作流检出时启用文件符号链接，并在测试前按 `.codex/rule-links.json` 修复与检查规则链接、文档路径和脚本命名空间，运行 `tools/Test-ValidationArtifacts.ps1` 的产物契约，再运行 `dotnet format whitespace FarmExchange.sln --verify-no-changes`。产物契约通过真实可执行夹具验证日志保全、干净分发 ZIP、启动非零及 60 秒超时，也检查本地 FPS 脚本的成功、失败、部分/零产物及三条 CI 和发布门禁；它不替代正式导出启动或远端 Actions 实际上传。静态检查范围见[CI 静态检查](../static-checks/ci.md)；链接映射及 Windows 修复步骤见[规则加载](../../.codex/rule-loading.md)。

## 三条独立 CI

| 工作流 | 自动触发 | 手动入口 | 职责与产物 |
| --- | --- | --- | --- |
| `ci.yml`（CI / 测试与覆盖率） | `dev`、`main` 推送，目标为 `dev` 的 PR | 有 | 静态检查、headless 测试、总体及逐模块 80% 行覆盖率、覆盖率报告 |
| `windows.yml`（Windows 构建） | `dev`、`main` 推送 | 有 | Release 编译、资源导入、Windows x86_64 导出、启动验收、干净构建与独立启动证据 |
| `macos.yml`（macOS 构建） | `dev`、`main` 推送 | 有 | Release 编译、资源导入、Universal 2 ZIP 导出、双架构与启动验收 |

同一次推送分别启动三条工作流，不互相等待。并发取消组分别为 `ci-`、`windows-`、`macos-` 加 `github.ref`，仅取消同一工作流同一分支的旧运行。目标为 `dev` 的 PR 按 GitHub 默认检出待合并结果，仅执行测试门禁；日常交付仍使用 `dev` → `main` PR，由人工合并。旧检查名「测试与导出」改为「测试与覆盖率」，配置了必需检查的分支保护须同步选择新名称。

三条工作流均从根目录 [global.json](../../global.json) 安装固定 .NET SDK 10.0.401，禁用版本前滚与预览版，项目目标为 `net10.0`，Godot 为 4.7.2 Mono。暂保留额外 `8.0.x` 安装，其中测试 CI 的 `dotnet-coverage` 使用 .NET 8 运行时；编译及导出仍选择固定 SDK 10。安装后输出 `dotnet --version`。依赖升级通过独立 issue 验证。

## Windows 构建与证据

独立 Windows 工作流在 `dev`、`main` 和手动运行中均执行完整导出。共享 `Invoke-WindowsDistribution.ps1` 复制同提交 schema 到 `logs/schema-v1.md`，真实启动导出程序并等待最多 60 秒，再保全 runtime/debug 会话并从分发目录移出，保持 schema、EXE/PCK/程序集和运行依赖。只有验收成功才上传 `FarmExchange-windows-x86_64-<sha>`，保留 14 天；它是提交级构建，正式版本包仍由发布工作流生成。

启动成功或失败均通过显式 `!cancelled()` 条件上传 `windows-validation-<sha>-<run-id>-<attempt>`，保存 stdout、stderr、`startup.json` 和实际日志到 `build/validation/windows-<run-id>-<attempt>/`。失败继续保持失败，未执行不上传旧证据，取消后不承诺上传。日志保存失败保留源文件并阻止构建上传。玩家首次启动仍生成自己的 runtime 日志。本地命令见 [Windows 构建](windows-build.md)。

macOS 上传 `FarmExchange-macos-universal-<sha>`，保留 14 天；ZIP、启动与签名约定见 [macOS 构建](macos-build.md)。

## 本地 FPS 验收

GitHub Actions 不提供 FPS 选项，不运行有窗口 FPS 测量，也不收集上传其报告。普通 `Run-Tests.ps1` 继续执行 headless 满地图 50 tick 及角落实体检查；图形测量使用本地 `Run-Tests.ps1 -Performance` 或独立 `Run-PerformanceTests.ps1 -GodotConsole`。

涉及地图、渲染或 UI 的修改，交付前必须本地运行 FPS 测试，使用至少 1920×1080，平均至少 60 FPS、P95 帧间隔不超过 16.67 ms，并保存前后截图。其他修改可按需执行。普通测试命令默认不启动 FPS，不代表免除适用改动的本地验收要求。

本地测量先清除三项固定旧产物，保留真实 JSON、前后截图及失败退出语义；可用 `Save-PerformanceEvidence.ps1` 显式保存实际文件与缺失清单，不能把阈值失败记为通过。操作见 [测试说明](testing.md)。

## 正式发布门禁

`release.yml` 仅从 `main` 手动发布：先核对同一提交的 `ci.yml`、`windows.yml`、`macos.yml` 三条工作流全部成功，再下载两平台同 SHA 产物。任一 CI 未成功、来源不符、产物缺失或过期都拒绝发布，不使用其他提交结果。

发布精确检出触发 SHA 的工具和 schema；Windows 再次启动验收、外移日志并检查干净 ZIP，macOS 保留已验收的原始 ZIP 字节。两份版本 ZIP 一起上传到单个 GitHub Release；不重复编译，无需本地上传。操作及失败处理见 [正式发布](release.md)。
