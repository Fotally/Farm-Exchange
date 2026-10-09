# macOS 构建与验收

项目使用 Godot 4.7.2 Mono 的 macOS Universal 2 模板，导出 `build/macos/FarmExchange.zip`。ZIP 内的 `.app` 同时包含 x86_64 与 arm64 可执行代码，以及两个架构各自的 `FarmExchange.dll`。预设位于 `export_presets.cfg`，bundle ID 为 `io.github.fotally.farmexchange`；`project.godot` 启用 ETC2 ASTC 纹理导入，以满足 arm64 导出要求。

## CI 流程

`.github/workflows/macos.yml` 在推送 `dev`、`main` 或手动触发时使用 `macos-15`：从根目录 [global.json](../../global.json) 安装固定的 .NET SDK 10.0.401，并输出实际选择版本；额外安装 .NET 8 SDK 只为提供 `net8.0` 兼容运行时。随后下载官方 Mono 编辑器与同版本导出模板，编译 C#，导入项目资源，导出 ZIP，再解压检查应用包、两个架构的程序集和 Universal 2 可执行文件，最后无窗口启动并检查退出状态。通过后上传 `FarmExchange-macos-universal-提交SHA`，保留 14 天。该工作流与 [Windows 主 CI](continuous-integration.md)分别运行，使用同一 SDK 约束。

在 macOS 本机安装相同版本的 Godot Mono 编辑器、.NET SDK 10.0.401 和 macOS 导出模板，另安装 .NET 8 SDK 提供测试与引擎所需的兼容运行时；项目目标框架保持 `net8.0`。在仓库根目录运行 `dotnet --version`，结果必须与 `global.json` 一致；运行 `dotnet --list-runtimes` 确认包含 `Microsoft.NETCore.App 8.0.x`。SDK 选择禁用版本前滚与预览版，缺少确切版本时明确失败。然后把 `NuGet.Config` 中的本地包源指向该编辑器随附的 `Godot.NET.Sdk` 包目录，再在仓库根目录执行：

```sh
dotnet nuget update source 'Godot 本地包' --source '/实际路径/GodotSharp/Tools/nupkgs' --configfile NuGet.Config
dotnet build FarmExchange.csproj --configuration Release
godot --headless --path . --import --quit
mkdir -p build/macos
godot --headless --path . --export-release macOS build/macos/FarmExchange.zip --quit
```

若编辑器可执行文件不在 `PATH` 中，将 `godot` 换为其 `.app/Contents/MacOS/` 内的可执行文件。导出路径是可覆盖的中间构建，不纳入 Git。

此预设使用内建临时签名，不进行 Apple 公证。CI 产物用于编译与启动验收；独立[发布工作流](release.md)复用同提交成功 CI 的原始 Universal 2 ZIP，只改成带版本号的附件名称，与 Windows ZIP 一起上传到同一个 Release，不解压重打包，保留包内容与 UNIX 权限信息。发布 runner 不在 Windows 上重新启动 macOS 应用，双架构与启动验收由该提交的 macOS CI 完成。当前未配置 Apple 公证，从网络下载后 macOS Gatekeeper 可能阻止打开；发布工作流不增加签名或公证服务。Godot 对 [macOS Universal 2、bundle ID 与签名的要求](https://docs.godotengine.org/en/4.7/tutorials/export/exporting_for_macos.html)及[命令行导出格式](https://docs.godotengine.org/en/4.7/tutorials/export/exporting_projects.html)是本配置依据。
