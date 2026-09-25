# Windows 构建与导出

## 构建来源

- 引擎：`E:\Godot\Godot_v4.7.2-stable_mono_win64` 中的 Godot 4.7.2 Mono。
- 官方 .NET 导出模板：同一目录中的 `Godot_v4.7.2-stable_mono_export_templates.tpz`；Windows x86_64 模板与中文所需 ICU 数据解压在 `export_templates/4.7.2.stable.mono/`，可供后续项目复用。
- `project.godot` 启用中文断行所需的文本服务数据；导出包应包含该数据。
- 项目导出配置：`export_presets.cfg`，排除测试场景、构建目录与覆盖率/FPS 报告目录；C# 构建配置：`FarmExchange.sln`、`FarmExchange.csproj`、`NuGet.Config`。Godot 的 .NET 导出要求项目根目录有与程序集同名的 `.sln`，Windows 发布首次运行还需从 nuget.org 获取 .NET 运行时包。

## 生成可运行版本

在项目根目录执行：

```text
dotnet build FarmExchange.csproj -c Release
E:\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe --headless --path . --export-release Windows build/windows/FarmExchange.exe --quit
```

`build/windows/` 是可覆盖且不纳入 Git 的 Windows x86_64 中间导出目录。导出后可在 PowerShell 中按以下方式验收启动：

```powershell
$executable = (Resolve-Path -LiteralPath build/windows/FarmExchange.exe).Path
$process = Start-Process -FilePath $executable -ArgumentList @('--headless', '--quit-after', '5') -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) {
    throw "导出程序启动失败，退出码：$($process.ExitCode)"
}
```

Windows GUI 程序经 PowerShell 的 `&` 直接调用后，`$LASTEXITCODE` 可能保持空值，不能据此判断启动是否成功；上面命令等待进程结束，并读取实际的 `ExitCode`。打包或移动时保持 `build/windows/` 中的全部文件完整。
