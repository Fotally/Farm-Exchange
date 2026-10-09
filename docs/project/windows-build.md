# Windows 构建与导出

## 构建来源

- 开发 SDK：安装 .NET SDK 10.0.401；仓库根目录的 [global.json](../../global.json) 是本地与 CI 的唯一 SDK 选择约束，禁用版本前滚与预览版。另安装 .NET 8 SDK 提供 `net8.0` 测试与 Godot 引擎所需的兼容运行时；项目目标框架保持 `net8.0`。
- 引擎：`E:\Godot\Godot_v4.7.2-stable_mono_win64` 中的 Godot 4.7.2 Mono。
- 官方 .NET 导出模板：同一目录中的 `Godot_v4.7.2-stable_mono_export_templates.tpz`；Windows x86_64 模板与中文所需 ICU 数据解压在 `export_templates/4.7.2.stable.mono/`，可供后续项目复用。
- `project.godot` 启用中文断行所需的文本服务数据；导出包应包含该数据。
- 项目导出配置：`export_presets.cfg`，排除测试场景、构建目录与覆盖率/FPS 报告目录；C# 构建配置：`FarmExchange.sln`、`FarmExchange.csproj`、`NuGet.Config`。Godot 的 .NET 导出要求项目根目录有与程序集同名的 `.sln`，Windows 发布首次运行还需从 nuget.org 获取 .NET 运行时包。
- Release / ExportRelease 不编译 development 目录与测试源码，发布资源也排除开发目录；Debug / ExportDebug 本地接入开发窗口。公共经营实现保持一份，开发倍率接口只在 DEBUG 存在；dev 本地构建与报告位置见[本地开发构建](development-build.md)，CI 不新增 dev 包。
- 所有构建配置统一排除 `build/**/*.cs`：该目录保留导出产物、验收副本和历史源码备份，不是编译输入。原有备份保持原位，实际脚本及测试源码仍遵循上述配置范围。

## 生成可运行版本

安装完成后，在项目根目录执行 `dotnet --version`，结果必须与 `global.json` 的版本一致；`dotnet --list-sdks` 列出已安装 SDK，`dotnet --list-runtimes` 应包含 `Microsoft.NETCore.App 8.0.x`。缺少确切 SDK 时安装该版本，不修改版本前滚策略。

在项目根目录执行：

```text
dotnet build FarmExchange.csproj -c Release
E:\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe --headless --path . --export-release Windows build/windows/FarmExchange.exe --quit
```

`build/windows/` 是可覆盖且不纳入 Git 的 Windows x86_64 中间导出目录。导出成功后，在 PowerShell 中复制当前构建对应的日志字段说明，随包保留为 exe 同级的 `logs/schema-v1.md`：

```powershell
New-Item -ItemType Directory -Path build/windows/logs -Force | Out-Null
Copy-Item -LiteralPath docs/project/runtime-log-schema-v1.md -Destination build/windows/logs/schema-v1.md -Force
```

Windows 发布运行生成的业务日志位于 `logs/runtime/*.log`，阅读规则见[日志 schema v1](runtime-log-schema-v1.md)。随后按以下方式验收启动：

```powershell
$executable = (Resolve-Path -LiteralPath build/windows/FarmExchange.exe).Path
$process = Start-Process -FilePath $executable -ArgumentList @('--headless', '--quit-after', '5') -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) {
    throw "导出程序启动失败，退出码：$($process.ExitCode)"
}
```

Windows GUI 程序经 PowerShell 的 `&` 直接调用后，`$LASTEXITCODE` 可能保持空值，不能据此判断启动是否成功；上面命令等待进程结束，并读取实际的 `ExitCode`。打包或移动时保持 `build/windows/` 中的全部文件完整。
