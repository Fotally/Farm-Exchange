# 开发者工具与双版本构建证据

历史调研说明：本文保留2026-10-05的实测事实及研究判断；#100 / #101 的现行操作以[参数化测试使用说明](../project/parameterized-tests-usage.md)为准。

调研日期：2026-10-05（北京时间）。关联 [#97](https://github.com/Fotally/Farm-Exchange/issues/97)，用于讨论设计；本次没有修改代码、构建配置或工作流，也没有执行导出。日志边界沿用 [#82 的设计](https://github.com/Fotally/Farm-Exchange/issues/82)及[日志 schema](https://github.com/Fotally/Farm-Exchange/issues/82)。用户已明确先讨论一套代码的架构，dev 仅本地构建、不进入 CI，可覆盖 Windows 与 macOS；具体工具与源码组织仍待讨论。

## 当前事实

| 项目 | 已读取证据 | 现状 |
| --- | --- | --- |
| C# 构建 | [项目文件](../../FarmExchange.csproj) | 使用 `Godot.NET.Sdk/4.7.2`、`net8.0`；没有自定义开发符号、编译文件排除或日志依赖 |
| 导出 | [导出预设](../../export_presets.cfg) | Windows x86_64 与 macOS Universal 2 各一个预设；`custom_features` 为空，资源排除 `tests/*,build/*,coverage/*` |
| Windows CI | [ci.yml](../../.github/workflows/ci.yml) | dev/main 运行测试；仅 main 执行普通 `Release` 编译、`--export-release` 导出、启动与上传 |
| macOS CI | [macos.yml](../../.github/workflows/macos.yml) | dev/main 都执行普通 `Release` 编译、`--export-release` 导出、双架构与启动验收 |
| 正式发布 | [release.yml](../../.github/workflows/release.yml) | 仅从 main 同提交成功的两平台 CI 下载产物；上传两份正式 ZIP，没有开发包发布入口 |
| 测试代码 | [测试入口](../../tests/TestSuite.cs)、项目文件 | `TestSuite.cs` 实际在 `tests/` 根下；C# 项目默认纳入测试源码，资源排除不等于排除测试类型 |

构建和发布的现行说明见[构建导航](../project/build-and-validation.md)、[Windows 构建](../project/windows-build.md)及[CI 说明](../project/continuous-integration.md)。Git 分支 `dev` 与交付物“开发版”是两个概念：main 同提交也可以同时生成开发版和正式版，无需为两种版本维护两份经营代码。

## C# 配置的精确区别

本地直接读取项目指定引擎内官方包：

```text
E:\Godot\Godot_v4.7.2-stable_mono_win64\GodotSharp\Tools\nupkgs\Godot.NET.Sdk.4.7.2.nupkg
包内：Sdk/Sdk.props、Sdk/Sdk.targets
```

并对当前项目分别执行只读 MSBuild 属性求值：

```text
dotnet msbuild FarmExchange.csproj -nologo -p:Configuration=Debug -getProperty:Configuration,DefineConstants,Optimize,GodotApiConfiguration
dotnet msbuild FarmExchange.csproj -nologo -p:Configuration=ExportDebug -getProperty:Configuration,DefineConstants,Optimize,GodotApiConfiguration
dotnet msbuild FarmExchange.csproj -nologo -p:Configuration=ExportRelease -getProperty:Configuration,DefineConstants,Optimize,GodotApiConfiguration
dotnet msbuild FarmExchange.csproj -nologo -p:Configuration=Release -getProperty:Configuration,DefineConstants,Optimize,GodotApiConfiguration
```

| 配置 | `DEBUG` | `TOOLS` | `Optimize` | `GodotApiConfiguration` | 用途 |
| --- | --- | --- | --- | --- | --- |
| `Debug` | 有 | 有 | false | Debug | 编辑器运行、现有 headless 场景测试 |
| `ExportDebug` | 有 | 无 | false | Debug | 导出的开发版 |
| `ExportRelease` | 无 | 无 | true | Release | 导出的正式版 |
| 普通 `Release` | 无 | 无 | true | Debug | 当前流程中的额外编译检查；与 Godot 正式导出配置不同 |

表中值是本地 4.7.2 包和当前项目属性的实测结果，不是从其他版本推断。SDK 声明的配置列表是 `Debug;ExportDebug;ExportRelease`；普通 `Release` 仍可求值，但不能把它当作 `ExportRelease`。官方文档也明确 `TOOLS` 对应编辑器的 `Debug` 配置。[Godot C# 编译符号说明](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_features.html#full-list-of-defines)

官方上游 C# 导出插件按导出的 `isDebug` 选择 `ExportDebug` 或 `ExportRelease`；SDK 源码分别补入 `DEBUG`、`TOOLS`。[导出插件源码](https://github.com/godotengine/godot/blob/master/modules/mono/editor/GodotTools/GodotTools/Export/ExportPlugin.cs)、[SDK targets 源码](https://github.com/godotengine/godot/blob/master/modules/mono/editor/Godot.NET.Sdk/Godot.NET.Sdk/Sdk/Sdk.targets)

这里的在线 `stable` 是滚动文档，`master` 是上游开发分支，都不是项目 4.7.2 的固定提交。项目 SDK 配置由上面的本地包与属性求值确认；实际导出插件执行的 publish 命令、最终程序集与资源内容仍需实施期用指定 4.7.2 引擎验收，不能把上游源码检查写成已经完成了本地双版本导出。

## 编译符号、运行时标签与模板判断

| 机制 | 判断的对象 | 能否直接剔除 C# 代码 | 对本项目的建议 |
| --- | --- | --- | --- |
| `#if DEBUG` | C# 编译时符号 | 能，未满足部分不参与编译 | 第一版用于开发工具类型、入口及专用经营命令 |
| `#if TOOLS` | 编辑器 C# 编译配置 | 能，但 `ExportDebug` 不含它 | 只用于真正的编辑器工具，不能用作导出开发版开关 |
| `OS.IsDebugBuild()` | 正在运行的 Godot 引擎模板 | 不能，仅运行时判断 | 显示/记录引擎构建环境；不能替代编译剔除 |
| `OS.HasFeature("debug")` | 引擎运行时内置标签，包含编辑器 | 不能 | 用于运行环境判断 |
| 预设 `custom_features="dev"` | 导出项目自定义运行时标签 | 不能自动产生 C# 符号 | 有独立交付物标识需求时再使用，不作为第一版必需配置 |

微软明确 `#if` 未满足的代码不编译；Godot 明确 `OS.IsDebugBuild()` 判断引擎模板，编辑器也返回 true。自定义 feature 仅在导出项目中生效，编辑器直接运行不读取预设中的自定义 feature。[微软条件编译](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/preprocessor-directives#conditional-compilation)、[Godot OS 判断](https://docs.godotengine.org/en/stable/classes/class_os.html#class-os-method-is-debug-build)、[Godot feature tags](https://docs.godotengine.org/en/stable/tutorials/export/feature_tags.html#custom-features)

第一版建议直接使用已有 `DEBUG`，把“开发版”定义为 `ExportDebug`，避免额外的 `DEV_TOOLS` 符号和重复开关。若以后确实需要“优化开启但仍带工具”的专项性能包，才讨论单独编译符号及其如何贯穿导出；当前没有该交付需求。开发包性能也不能代替正式包性能验收，因为两种配置的优化和诊断开销不同。

## 正式版剔除要分代码和资源两层

建议将开发工具集中在一个目录，整个类型与实际入口用 `#if DEBUG`；经营入口中确有需要的开发专用方法同样条件编译。普通经营查询和命令保持原有接口。关闭窗口、隐藏快捷键或 `if (OS.IsDebugBuild())` 不足以证明代码已从 DLL 移除。[微软条件编译](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/preprocessor-directives#conditional-compilation)

整目录 C# 排除也可用条件化的 `<Compile Remove="目录/**/*.cs" />`；不需要同时为同一工具目录叠加两套排除。测试源码则适合在两个导出配置中都 `Compile Remove="tests/**/*.cs"`，保留编辑器 `Debug` 的测试编译。正式资源预设另外排除开发专用场景和素材；共享主场景不能序列化引用正式包不存在的开发工具类型。[微软排除编译项](https://learn.microsoft.com/en-us/visualstudio/msbuild/how-to-exclude-files-from-the-build?view=vs-2022)、[Godot 导出资源筛选](https://docs.godotengine.org/en/stable/tutorials/export/exporting_projects.html#resource-options)

当前只读执行 `dotnet msbuild FarmExchange.csproj -nologo -p:Configuration=ExportRelease -getItem:Compile`，结果仍包含 `tests/e2e/*.cs` 等测试源码；项目文件没有 `Compile Remove`。因此不能声称现有正式程序集已经排除测试代码。此项是拟议构建整理范围，需要在实施 issue 中明确；本文没有修改它，也没有检查已有 DLL 的类型清单。

不建议为此开启整个游戏程序集裁剪或引入独立工具 DLL；当前 `EnableDynamicLoading=true` 与 Godot 场景类型加载已有实际需求，用明确条件编译及资源过滤即可。Release 留下通用经营能力是正常的，验收目标是开发专用入口、类型、命令实现和资源不存在。

## 同提交生成两种交付物

官方命令行明确 `--export-debug` 使用 Debug 模板，`--export-release` 使用 Release 模板，两者都需要匹配预设与模板。[Godot 命令行导出](https://docs.godotengine.org/en/stable/tutorials/editor/command_line_tutorial.html#exporting)

建议保留现有 `Windows`、`macOS` 名称作为正式预设，新增 `Windows Dev`、`macOS Dev`，主要差异是资源过滤和输出目录。平台架构、图像配置及签名规则共用现行值。尚未新增独立资源时，同一平台预设也能被两条导出命令复用；需要正式包排除开发资源时，分别维护预设更直观。

```text
godot --headless --path . --export-debug "Windows Dev" build/dev/windows/FarmExchange.exe --quit
godot --headless --path . --export-release Windows build/windows/FarmExchange.exe --quit
godot --headless --path . --export-debug "macOS Dev" build/dev/macos/FarmExchange.zip --quit
godot --headless --path . --export-release macOS build/macos/FarmExchange.zip --quit
```

这是未创建预设的拟议命令，`godot` 代表项目指定 4.7.2 Mono 的可执行文件。开发与正式包各在独立目录，保留现有 `build/windows/` 正式中间导出约定；同一工作区顺序导出，避免并发写入 `.godot` 和 Godot 发布缓存。

建议一次本地构建入口完成两个版本，复用安装、导入及测试步骤，不增加第三套配置。显式预编译检查如果保留，应检查 `ExportDebug`、`ExportRelease`；Godot 导出自身仍负责最终目标平台 publish，不能只复制前一次普通 `Release` 编译得到的 DLL。

用户已明确 dev 目前仅在本地构建，不进入 CI，也不上传开发工件；撤回前轮双包 CI 的建议。两个平台通过预设选择产物类型，共用同一套经营和开发工具源码。Windows dev 包在 Windows 环境验收启动，macOS dev 包在 macOS 环境检查 Universal 2、x86_64/arm64 程序集并启动；能导出某平台的包不等于能在当前主机运行它。现有 CI 继续导出 release，正式发布沿用 main 同提交成功、两平台齐备与人工指定版本号规则。

## 与日志的衔接及必要验收

[#82](https://github.com/Fotally/Farm-Exchange/issues/82)已确认开发构建额外有详细日志、正式构建仅关键日志。建议日志和开发工具共用上面的编译配置含义：编辑器 `Debug`、导出 `ExportDebug` 都支持详细采集，`ExportRelease` 关闭详细采集；关闭工具窗口不切换构建类型。现有 schema 的 `BuildKind=Debug/Release` 可继续对应这两类构建，不因包名叫 dev 就擅自变更字段枚举。

实施后应核对：

- 两份包来自同一 SHA；构建标识能分辨开发/正式，环境显示与实际模板一致。
- 开发包能实际打开工具、执行已确认命令；正式包的快捷键及参数不会开启工具，程序集没有专用类型/方法，PCK 没有开发专用资源或测试场景。
- 两种导出配置的编译项都没有测试源码；编辑器测试入口保持可运行，完整场景测试与逐模块覆盖率门槛保持。
- 正式包的生产、日期、冻结库存和资金行为与未操作工具的开发包一致；使用工具修改状态后的局不能充当正常经营验收证据。
- 正式包单独执行 FPS/负载与两平台启动验收；不凭开发包帧率推定正式包表现。

双版本可行性已有本地配置和官方机制证据，但实际工具入口、操控范围、开发资源筛选、导出类型清单及双平台打包仍属于设计确认后的实施验收。
