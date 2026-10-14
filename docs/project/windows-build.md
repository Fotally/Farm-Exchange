# Windows 构建与导出

## 构建来源

- 开发 SDK：安装 .NET SDK 10.0.401；仓库根目录的 [global.json](../../global.json) 是本地与 CI 的唯一 SDK 选择约束，禁用版本前滚与预览版。项目目标框架为 `net10.0`，游戏使用 .NET 10 运行时；完整测试中的 `dotnet-coverage` 工具仍需 .NET 8 运行时。
- 引擎：`E:\Godot\Godot_v4.7.2-stable_mono_win64` 中的 Godot 4.7.2 Mono。
- 官方 .NET 导出模板：同一目录中的 `Godot_v4.7.2-stable_mono_export_templates.tpz`；Windows x86_64 模板与中文所需 ICU 数据解压在 `export_templates/4.7.2.stable.mono/`，可供后续项目复用。
- `project.godot` 启用中文断行所需的文本服务数据；导出包应包含该数据。
- 项目导出配置：`export_presets.cfg`，排除测试场景、构建目录与覆盖率/FPS 报告目录；C# 构建配置：`FarmExchange.sln`、`FarmExchange.csproj`、`NuGet.Config`。Godot 的 .NET 导出要求项目根目录有与程序集同名的 `.sln`，Windows Release 首次导出还需从 nuget.org 获取 .NET 10 的 Windows 运行包；导出后须同时检查 exe、PCK 和 `data_FarmExchange_windows_x86_64/FarmExchange.dll`。
- Release / ExportRelease 不编译 development 目录与测试源码，发布资源也排除开发目录；Debug / ExportDebug 本地接入开发窗口。公共经营实现保持一份，开发倍率接口只在 DEBUG 存在；dev 本地构建与报告位置见[本地开发构建](development-build.md)，CI 不新增 dev 包。
- 所有构建配置统一排除 `build/**/*.cs`：该目录中的导出、测试工具工件及历史源码不是编译输入，实际脚本及测试源码遵循上述配置范围。

## 生成可运行版本

安装完成后，在项目根目录执行 `dotnet --version`，结果必须与 `global.json` 的版本一致；`dotnet --list-sdks` 列出已安装 SDK，`dotnet --list-runtimes` 应包含游戏所需的 `Microsoft.NETCore.App 10.0.x`；运行完整覆盖率测试时还需 `Microsoft.NETCore.App 8.0.x`。缺少确切 SDK 时安装该版本，不修改版本前滚策略。

在项目根目录执行：

```text
dotnet build FarmExchange.csproj -c Release
E:\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe --headless --path . --export-release Windows build/windows/FarmExchange.exe --quit
```

本地验收直接使用当前 `dev` 工作区，Windows Release 按上面的命令覆盖导出到 `build/windows/FarmExchange.exe`。不为验收或导出失败回滚另建项目、源码树或发布包副本；回滚依据 Git 提交和 GitHub 已发布产物。正常测试报告、覆盖率、截图及必要测试工具工件继续保存到既有产物目录。`build/windows/` 不纳入 Git。

导出成功后，在 PowerShell 中调用完整分发准备命令；证据目录每次使用独立名字，例如时间戳或 CI 的运行号与尝试次数：

```powershell
./tools/Invoke-WindowsDistribution.ps1 `
    -BuildDirectory build/windows `
    -SchemaPath docs/project/runtime-log-schema-v1.md `
    -EvidenceDirectory ('build/validation/windows-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
```

命令检查有效 EXE、PCK 和程序集，将本提交 schema 原字节复制到 `logs/schema-v1.md`，真实运行 `--headless --quit-after 5`，最多等待 60 秒。stdout、stderr、启动退出事实和实际存在的 runtime/debug 日志保存在证据目录；超时会先终止进程并等待退出。日志逐文件复制并校验成功后才从构建移出；保存失败保留未保存的源日志并停止分发，启动失败也继续保留原错误。

仅 `logs/runtime/`、`logs/debug/` 是需要外移的验收会话，schema 与其他运行文件原样保留。准备后的目录必须无这两类会话文件，schema SHA256 与本次源一致，全部必需文件存在且非空。命令用于受控构建目录，不能指向玩家已解压的游戏；证据目录与构建目录不得相同或互相包含，schema 源必须来自构建目录之外，已有证据目录拒绝覆盖。

正式包或本地 ZIP 验收可附加 `-ArchivePath`，例如：

```powershell
./tools/Invoke-WindowsDistribution.ps1 `
    -BuildDirectory build/windows `
    -SchemaPath docs/project/runtime-log-schema-v1.md `
    -EvidenceDirectory ('build/validation/windows-zip-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')) `
    -ArchivePath build/releases/local/FarmExchange-windows-x86_64.zip
```

ZIP 路径必须位于构建和证据目录之外，已有最终包拒绝覆盖。命令先生成临时 ZIP，直接检查其 EXE/PCK/程序集、schema 字节和无 runtime/debug 会话，再发布为最终路径；失败不留下冒充已验收的最终包。CI 上传目录与正式发布第二次启动使用同一命令，避免任一处启动日志混入玩家输入。

Windows GUI 程序经 PowerShell 的 `&` 直接调用后，`$LASTEXITCODE` 可能保持空值；分发命令等待真实进程退出，并读取实际 `ExitCode`。玩家从干净包首次启动后仍在 exe 同级 `logs/runtime/*.log` 生成本次业务会话，阅读规则见[日志 schema v1](runtime-log-schema-v1.md)。工具契约检查命令为 `./tools/Test-ValidationArtifacts.ps1`；它构建真实可执行的受控夹具，验证启动失败、60 秒超时、日志保存失败、干净 ZIP 及首次启动行为，不替代上述正式 Windows 导出程序验收。

#110 四个正式／开发导出预设均在 `include_filter` 显式包含 `assets/gameplay/terrain/terrain_rules.json` 和 `assets/gameplay/terrain/shoreline_templates.json`。它们是地形正式运行资源，不能只依赖编辑器中的文件；PNG 与 shader 沿正常资源依赖导出。导出启动须用默认含水局验证资源完整，不通过开发纯陆地输入绕过生成。
