# 本地开发构建与工具排除

关联 [#100](https://github.com/Fotally/Farm-Exchange/issues/100) 与 [#101](https://github.com/Fotally/Farm-Exchange/issues/101)。项目只维护一套公共经营代码与一个 `FarmExchange.csproj`，不创建第二个工具程序集，不把 dev 包加入 CI。

| 构建配置 | 编译内容 |
| --- | --- |
| `Debug` / `ExportDebug` | 公共经营、开发工具和测试源码；`DEBUG` 场景组装接入开发窗口，时间模块增加开发倍率入口。 |
| `Release` / `ExportRelease` | 移除 `scripts/**/development/**/*.cs` 和 `tests/**/*.cs`；场景组装的开发引用及时间模块额外倍率接口均不编译。公共 0.5×、1×、2×与批量经营保留。 |

正式 `Windows` 与 `macOS` 预设排除 tests、build、coverage、开发脚本与专用资源目录。本地 `Windows Dev` 与 `macOS Dev` 预设排除 tests、build、coverage 和专用验收图，但保留开发节点所需脚本资源。Godot 创建 C# 节点时会按程序集中的脚本路径加载 `DeveloperToolsWindow.cs`，仅保留开发程序集不足以创建工具窗口；不能使用正式预设的开发资源排除列表生成 dev 包。两类导出都不附带测试 JSON 或报告，开发包加载用户选择的持久配置文件。

在仓库根目录本地生成 Windows dev 包：

```powershell
dotnet build FarmExchange.csproj -c Debug
New-Item -ItemType Directory -Force -Path build/dev/windows
& 'E:/Godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' --headless --path . --export-debug 'Windows Dev' build/dev/windows/FarmExchange.exe --quit
```

macOS 本地先按[macOS 构建说明](macos-build.md)配置同版本编辑器与本地 SDK 包源，再在仓库根目录创建输出目录并使用 dev 预设：

```sh
mkdir -p build/dev/macos
godot --headless --path . --export-debug 'macOS Dev' build/dev/macos/FarmExchange.zip --quit
```

`godot` 使用已配置的 Mono 编辑器；不在 `PATH` 时换为其 `.app/Contents/MacOS/` 内可执行文件。CI 仍按既有正式预设与发布构建执行；开发包不进入正式 GitHub Release，不通过临时切换预设内容改变资源权限。

开发窗口入口与使用见[开发窗口接口](../architecture/ui/developer-tools-window/interface-developer-tools-window.md)。编辑器/仓库运行报告写入 `build/test-runs/<run-id>/report.json`；dev 包写入包外 `reports/test-runs/<run-id>/report.json`。Windows 根目录来自 exe，macOS 来自 `.app` 父目录；路径不可写即明确拒绝，不转移到其他位置。

构建验收需核对不同配置实际编译输入和程序集类型、正式包资源排除、dev 包开发脚本存在、真实窗口与流程报告。导出必须等待引擎实际 Exit 0；`savepack` 的完成日志不代表进程已正常结束。Windows 原生窗口探针按物理像素验证1080P时须使用 DPI 感知线程，避免系统缩放将1920×1080虚拟为1280×720。本文描述实际配置和验收方法，最终通过证据由任务计划记录。
