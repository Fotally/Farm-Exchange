# macOS 构建与验收

项目使用 Godot 4.7.2 Mono 的 macOS Universal 2 模板，导出 `build/macos/FarmExchange.zip`。ZIP 内的 `.app` 同时包含 x86_64 与 arm64 可执行代码，以及两个架构各自的 `FarmExchange.dll`。预设位于 `export_presets.cfg`，bundle ID 为 `io.github.fotally.farmexchange`；`project.godot` 启用 ETC2 ASTC 纹理导入，以满足 arm64 导出要求。

## CI 流程

`.github/workflows/macos.yml` 在推送 `dev`、`main` 或手动触发时使用 `macos-15`：下载官方 Mono 编辑器与同版本导出模板，安装 .NET 8，编译 C#，导入项目资源，导出 ZIP，再解压检查应用包、两个架构的程序集和 Universal 2 可执行文件，最后无窗口启动并检查退出状态。通过后上传 `FarmExchange-macos-universal-提交SHA`，保留 14 天。该工作流与 [Windows 主 CI](continuous-integration.md)分别运行。

在 macOS 本机安装相同版本的 Godot Mono 编辑器、.NET 8 和 macOS 导出模板后，先把 `NuGet.Config` 中的本地包源指向该编辑器随附的 `Godot.NET.Sdk` 包目录，再在仓库根目录执行：

```sh
dotnet nuget update source 'Godot 本地包' --source '/实际路径/GodotSharp/Tools/nupkgs' --configfile NuGet.Config
dotnet build FarmExchange.csproj --configuration Release
godot --headless --path . --import --quit
mkdir -p build/macos
godot --headless --path . --export-release macOS build/macos/FarmExchange.zip --quit
```

若编辑器可执行文件不在 `PATH` 中，将 `godot` 换为其 `.app/Contents/MacOS/` 内的可执行文件。导出路径是可覆盖的中间构建，不纳入 Git。

此预设使用内建临时签名，不进行 Apple 公证。CI 产物用于编译与启动验收；从网络下载后直接分发时，macOS Gatekeeper 可能阻止打开。正式版本仍遵守[发布流程](release.md)，当前只发布 Windows 压缩包。Godot 对 [macOS Universal 2、bundle ID 与签名的要求](https://docs.godotengine.org/en/4.7/tutorials/export/exporting_for_macos.html)及[命令行导出格式](https://docs.godotengine.org/en/4.7/tutorials/export/exporting_projects.html)是本配置依据。
