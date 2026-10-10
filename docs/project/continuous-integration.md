# GitHub Actions 持续集成

工作流检出时启用文件符号链接，并在测试前按 `.codex/rule-links.json` 修复与检查规则链接、文档路径和脚本命名空间，再运行 `dotnet format whitespace FarmExchange.sln --verify-no-changes`。静态检查范围见[CI 静态检查](../static-checks/ci.md)；链接映射及 Windows 修复步骤见[规则加载](../../.codex/rule-loading.md)。

`.github/workflows/ci.yml` 使用 Windows 托管运行器、由根目录 [global.json](../../global.json) 固定的 .NET SDK 10.0.401 和 Godot 4.7.2 Mono，并按以下规则触发：

| 触发分支 | 编译与 headless 场景测试 | 80% 行覆盖率门槛 | 图形 FPS 性能测试 | Windows Release 导出与启动 | 上传产物 |
| --- | --- | --- | --- | --- | --- |
| 推送到 `dev` | 是 | 是 | 否 | 否 | Cobertura 报告，保留 14 天 |
| 推送到 `main`（包括人工合并 PR） | 是 | 是 | 否 | 是 | 覆盖率报告及完整 Windows x86_64 目录，保留 14 天 |
| 手动 `workflow_dispatch` 并勾选 `performance` | 是 | 是 | 是 | 仅在 `main` 执行 | 覆盖率与 FPS JSON 报告，保留 14 天 |

Windows 与 macOS 工作流都通过 `actions/setup-dotnet` 的 `global-json-file` 读取同一 SDK 版本；`global.json` 禁用版本前滚与预览版，仓库内的编译、格式检查和工具命令必须选择该确切 SDK，缺少时明确失败。项目目标框架为 `net10.0`；双平台 CI 除固定的 SDK 10.0.401 外暂保留 `8.0.x` 安装，其中 Windows 覆盖率工具 `dotnet-coverage` 仍以 `net8.0` 发布并使用 .NET 8 运行时。安装步骤后续均输出 `dotnet --version` 作为实际版本证据。

Godot、`dotnet-coverage` 和 GitHub 官方 Action 固定到明确主版本或工具版本。升级 SDK 时修改唯一约束 `global.json` 并同步安装说明；升级这些依赖均应通过独立 issue，在 `dev` 验证成功后再合并。`main` 的导出程序启动步骤使用进程退出码检查，成功后才上传 Windows 构建；产物名包含提交 SHA，下载后应保持目录结构完整。它是提交级验收产物，不替代带版本号的正式 Release 压缩包。

Windows Release 导出成功后，CI 将该提交的 `docs/project/runtime-log-schema-v1.md` 复制到构建目录的 `logs/schema-v1.md`，随完整 Windows 产物上传。运行产生的业务日志位于 exe 同级 `logs/runtime/*.log`；本地导出也需执行相同复制步骤，见[Windows 构建与导出](windows-build.md)。

`.github/workflows/macos.yml` 是独立的 macOS 构建检查，在 `dev`、`main` 推送和手动触发时运行。它使用 macOS runner 编译 C#、导出 Universal 2 ZIP、检查双架构程序集并启动应用；通过后上传 ZIP，保留 14 天。详细命令、产物用途和签名范围见[macOS 构建与验收](macos-build.md)。

`.github/workflows/release.yml` 提供独立的「发布游戏版本」手动入口：选择 `main` 并输入版本号，要求触发时同提交的 Windows 主 CI 与 macOS CI 均成功，下载两平台产物；Windows 再验收启动并完整打包，macOS 检查 Universal 2 ZIP 后保留原包字节，两份 ZIP 一起上传到同一个 GitHub Release。任一平台无成功 CI、产物缺失或过期、包不完整都停止发布；无需本地上传，不重复编译，也不使用其他提交的产物。操作与失败处理见[正式版本发布](release.md)。
