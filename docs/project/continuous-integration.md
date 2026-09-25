# GitHub Actions 持续集成

工作流检出时启用文件符号链接，并在测试前按 `.codex/rule-links.json` 修复与检查规则链接、文档路径和脚本命名空间，再运行 `dotnet format whitespace FarmExchange.sln --verify-no-changes`。静态检查范围见[CI 静态检查](../static-checks/ci.md)；链接映射及 Windows 修复步骤见[规则加载](../../.codex/rule-loading.md)。

`.github/workflows/ci.yml` 使用 Windows 托管运行器、.NET 8 和 Godot 4.7.2 Mono，并按以下规则触发：

| 触发分支 | 编译与 headless 场景测试 | 80% 行覆盖率门槛 | 图形 FPS 性能测试 | Windows Release 导出与启动 | 上传产物 |
| --- | --- | --- | --- | --- | --- |
| 推送到 `dev` | 是 | 是 | 否 | 否 | Cobertura 报告，保留 14 天 |
| 推送到 `main`（包括人工合并 PR） | 是 | 是 | 否 | 是 | 覆盖率报告及完整 Windows x86_64 目录，保留 14 天 |
| 手动 `workflow_dispatch` 并勾选 `performance` | 是 | 是 | 是 | 仅在 `main` 执行 | 覆盖率与 FPS JSON 报告，保留 14 天 |

工作流中的 Godot、.NET、`dotnet-coverage` 和 GitHub 官方 Action 均固定到明确主版本或工具版本。升级这些依赖时应通过独立 issue 修改版本，并在 `dev` 验证成功后再合并。`main` 的导出程序启动步骤使用上面的进程退出码检查，成功后才上传 Windows 构建；产物名包含提交 SHA，下载后应保持目录结构完整。它是提交级验收产物，不替代带版本号的正式 Release 压缩包。

`.github/workflows/macos.yml` 是独立的 macOS 构建检查，在 `dev`、`main` 推送和手动触发时运行。它使用 macOS runner 编译 C#、导出 Universal 2 ZIP、检查双架构程序集并启动应用；通过后上传 ZIP，保留 14 天。详细命令、产物用途和签名范围见[macOS 构建与验收](macos-build.md)。
