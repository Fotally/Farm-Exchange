# 开发工具的 C# 公开源码实践案例

历史调研说明：本文保留2026-10-05的实测事实及研究判断；#100 / #101 的现行接入与验收以[参数化测试主计划](../project/developer-tools-parameterized-tests.md)为准。

调研日期：2026-10-05。用于 [#97](https://github.com/Fotally/Farm-Exchange/issues/97) 的架构讨论，尚未授权实施。证据来自项目源码和构建脚本，链接固定到调研时的提交；没有把类名中的 `Debug` 当作发行排除证据，也没有检查下载后的发行程序集。

本项目已确认：经营代码只维护一套；dev 只在本地构建、不上 CI；Windows 和 macOS 是构建平台选择。以下案例用来比较代码组织，不代表采用它们的发布策略。

## 一、Nez：集中生命周期入口，开发功能按文件分组

源码基准：`prime31/Nez@2862974e423f1dccaed4887673cd5c241faa1c55`，MonoGame/FNA 的 C# 游戏框架。

1. **代码结构。** `Debug/Console/DebugConsole.cs` 管输入、显示和命令解析，`DefaultCommands.cs` 通过同一个 `partial class DebugConsole` 提供内置命令；对象检查器另放 `Debug/Inspector/RuntimeInspector.cs`。开发代码集中在目录，正常主循环仍在 `Core.cs`。[控制台](https://github.com/prime31/Nez/blob/2862974e423f1dccaed4887673cd5c241faa1c55/Nez.Portable/Debug/Console/DebugConsole.cs#L13-L85)、[内置命令](https://github.com/prime31/Nez/blob/2862974e423f1dccaed4887673cd5c241faa1c55/Nez.Portable/Debug/Console/DefaultCommands.cs#L27-L76)
2. **访问游戏状态。** `[Command]` 标记静态方法，控制台扫描程序集并建立命令到方法的调用。`inspect` 从现有 `Core.Scene.FindEntity` 取得真实实体，统计命令读取当前场景，时间命令写现有 `Time.TimeScale`，没有开发版场景状态副本。[注册实现](https://github.com/prime31/Nez/blob/2862974e423f1dccaed4887673cd5c241faa1c55/Nez.Portable/Debug/Console/DebugConsole.cs#L612-L684)、[状态访问](https://github.com/prime31/Nez/blob/2862974e423f1dccaed4887673cd5c241faa1c55/Nez.Portable/Debug/Console/DefaultCommands.cs#L138-L218)
3. **编译与发布。** `Core` 的开发更新和绘制方法标记 `[Conditional("DEBUG")]`，方法体另用 `#if DEBUG`；无 DEBUG 的调用位置会省略调用。内置命令文件与 `RuntimeInspector` 类整体受 `#if DEBUG` 限制。但 `DebugConsole.cs` 本体没有整体条件编译，`Nez.csproj` 也没有按 Release 排除控制台文件，因此不能称为“Release 不包含控制台类型”。它是开发调用及部分功能排除，而不是整套工具源码排除。[主循环钩子](https://github.com/prime31/Nez/blob/2862974e423f1dccaed4887673cd5c241faa1c55/Nez.Portable/Core.cs#L335-L375)、[检查器](https://github.com/prime31/Nez/blob/2862974e423f1dccaed4887673cd5c241faa1c55/Nez.Portable/Debug/Inspector/RuntimeInspector.cs#L8-L11)、[项目配置](https://github.com/prime31/Nez/blob/2862974e423f1dccaed4887673cd5c241faa1c55/Nez.Portable/Nez.csproj#L23-L33)
4. **对 Farm Exchange 的启发。** 可借鉴“开发代码集中、正常循环只保留少数接入点、partial 拆职责”。本项目已有具体经营接口，首版无需扫描所有程序集或建设反射命令系统；若要求发行程序集完全没有开发工具，需要比 Nez 更明确的文件排除边界。这是本项目适配判断。

## 二、Thrive：Godot C# 的工具界面与真实模拟共用

源码基准：`Revolutionary-Games/Thrive@6e4e315057ef0c4cd930ce5f90a95c6dea514ca2`。

1. **代码结构。** `DebugOverlays` 是 Godot `Control`，按 `DebugOverlays.Inspector.cs`、`FPSCounter.cs`、`PerformanceMetrics.cs` 等拆成同一个 partial 类；控制台显示与日志/命令管理分开。作弊界面有通用 `CheatMenu` 与阶段专用 `MicrobeCheatMenu`，模拟仍在 `MicrobeStage`。[覆盖层](https://github.com/Revolutionary-Games/Thrive/blob/6e4e315057ef0c4cd930ce5f90a95c6dea514ca2/src/engine/DebugOverlays.cs#L4-L23)、[检查器分文件](https://github.com/Revolutionary-Games/Thrive/blob/6e4e315057ef0c4cd930ce5f90a95c6dea514ca2/src/engine/DebugOverlays.Inspector.cs#L1-L7)、[阶段界面](https://github.com/Revolutionary-Games/Thrive/blob/6e4e315057ef0c4cd930ce5f90a95c6dea514ca2/src/microbe_stage/MicrobeCheatMenu.cs)
2. **访问游戏状态。** 界面调用 `CheatManager.SpawnEnemy()` 等具名方法；管理器发 C# 事件，正在运行的 `MicrobeStage` 订阅并在退出时解除。阶段处理器调用真实的 `SpawnHelpers` 和 `WorldSimulation.SpawnSystem`，完成外部出生通知及最终提交。其布尔作弊状态也由正常模拟读取，并非所有功能都是命令事件。[界面意图](https://github.com/Revolutionary-Games/Thrive/blob/6e4e315057ef0c4cd930ce5f90a95c6dea514ca2/src/microbe_stage/MicrobeCheatMenu.cs#L68-L80)、[事件发送](https://github.com/Revolutionary-Games/Thrive/blob/6e4e315057ef0c4cd930ce5f90a95c6dea514ca2/src/engine/CheatManager.cs#L220-L240)、[订阅生命周期](https://github.com/Revolutionary-Games/Thrive/blob/6e4e315057ef0c4cd930ce5f90a95c6dea514ca2/src/microbe_stage/MicrobeStage.cs#L363-L385)、[真实生成路径](https://github.com/Revolutionary-Games/Thrive/blob/6e4e315057ef0c4cd930ce5f90a95c6dea514ca2/src/microbe_stage/MicrobeStage.cs#L2505-L2538)
3. **编译与发布。** `Thrive.csproj` 在 `ExportRelease` 排除测试源码和 gdUnit，并条件排除测试包引用；打包脚本调用 `--export-release`。作弊菜单由 `Settings.Instance.CheatsEnabled` 运行时控制，相关工具源码没有整体 `#if DEBUG`。导出预设排除测试及打包脚本等资源，没有排除上述引擎工具；`project.godot` 还直接自动加载控制台管理器。证据支持“正常发行路径保留这些工具，运行时控制部分入口”，不能拿它证明开发工具在发行时被编译剔除。[编译配置](https://github.com/Revolutionary-Games/Thrive/blob/6e4e315057ef0c4cd930ce5f90a95c6dea514ca2/Thrive.csproj#L18-L50)、[发行导出](https://github.com/Revolutionary-Games/Thrive/blob/6e4e315057ef0c4cd930ce5f90a95c6dea514ca2/Scripts/PackageTool.cs#L404)、[运行时开关](https://github.com/Revolutionary-Games/Thrive/blob/6e4e315057ef0c4cd930ce5f90a95c6dea514ca2/src/gui_common/CheatMenu.cs#L15-L33)、[资源预设](https://github.com/Revolutionary-Games/Thrive/blob/6e4e315057ef0c4cd930ce5f90a95c6dea514ca2/export_presets.cfg#L9-L10)、[控制台自动加载](https://github.com/Revolutionary-Games/Thrive/blob/6e4e315057ef0c4cd930ce5f90a95c6dea514ca2/project.godot#L32-L35)
4. **对 Farm Exchange 的启发。** 最贴近本项目的是“Godot UI 发意图，现有协调/模拟模块处理，真实模块负责状态”，以及工具 UI 按职责拆文件。其全局静态作弊状态会让正常模拟持续感知作弊开关；本项目如只需单次调试操作，可使用明确方法调用，不必照搬全局事件总线、继承层次或作弊状态分支。这是本项目适配判断。

## 三、Barotrauma：共享源码，平台项目配置，部分命令开发专用

源码基准：`FakeFishGames/Barotrauma@e6e95da83b1b5ca8b55ec52653c7cf6b40e30247`。

1. **代码结构。** 控制台使用同一 `static partial class DebugConsole`，分别放在 `SharedSource`、`ClientSource`、`ServerSource`；共享命令处理与客户端显示/行为分开。Windows/Mac 客户端项目编入同一个共享目录，平台差异由项目依赖和符号选择，未复制公共游戏源码。[共享类](https://github.com/FakeFishGames/Barotrauma/blob/e6e95da83b1b5ca8b55ec52653c7cf6b40e30247/Barotrauma/BarotraumaShared/SharedSource/DebugConsole.cs#L40-L67)、[Windows 共享编译项](https://github.com/FakeFishGames/Barotrauma/blob/e6e95da83b1b5ca8b55ec52653c7cf6b40e30247/Barotrauma/BarotraumaClient/WindowsClient.csproj#L75-L76)、[Mac 共享编译项](https://github.com/FakeFishGames/Barotrauma/blob/e6e95da83b1b5ca8b55ec52653c7cf6b40e30247/Barotrauma/BarotraumaClient/MacClient.csproj#L69-L70)
2. **访问游戏状态。** 命令持有 `Action<string[]> OnExecute`，先执行作弊允许检查，再调用处理器。`spawnitem` 进入同一控制台的生成实现，客户端从真实 `Character.Controlled` 和当前镜头取输入；`enablecheats` 也修改当前游戏会话的标记。这里更多是命令直接访问游戏对象，并没有统一的独立开发工具访问接口。[命令执行](https://github.com/FakeFishGames/Barotrauma/blob/e6e95da83b1b5ca8b55ec52653c7cf6b40e30247/Barotrauma/BarotraumaShared/SharedSource/DebugConsole.cs#L70-L86)、[生成命令](https://github.com/FakeFishGames/Barotrauma/blob/e6e95da83b1b5ca8b55ec52653c7cf6b40e30247/Barotrauma/BarotraumaShared/SharedSource/DebugConsole.cs#L316-L331)、[真实状态访问](https://github.com/FakeFishGames/Barotrauma/blob/e6e95da83b1b5ca8b55ec52653c7cf6b40e30247/Barotrauma/BarotraumaShared/SharedSource/DebugConsole.cs#L3190-L3198)
3. **编译与发布。** Windows `Debug` 配置定义 DEBUG，`Release` 没有；DEBUG 使作弊默认开启，普通命令与 `enablecheats` 不受 DEBUG 整体排除，只有部分专项命令在 `#if DEBUG` 内。发布脚本把平台项目、配置、运行时传给 `dotnet publish`。所以 Release 是同一代码集的配置构建，仍保留可运行时开启的控制台；不等于发行版去除全部开发能力。[配置符号](https://github.com/FakeFishGames/Barotrauma/blob/e6e95da83b1b5ca8b55ec52653c7cf6b40e30247/Barotrauma/BarotraumaClient/WindowsClient.csproj#L20-L53)、[默认作弊状态](https://github.com/FakeFishGames/Barotrauma/blob/e6e95da83b1b5ca8b55ec52653c7cf6b40e30247/Barotrauma/BarotraumaShared/SharedSource/DebugConsole.cs#L180-L185)、[运行时开启](https://github.com/FakeFishGames/Barotrauma/blob/e6e95da83b1b5ca8b55ec52653c7cf6b40e30247/Barotrauma/BarotraumaClient/ClientSource/DebugConsole.cs#L516-L527)、[部分开发命令](https://github.com/FakeFishGames/Barotrauma/blob/e6e95da83b1b5ca8b55ec52653c7cf6b40e30247/Barotrauma/BarotraumaClient/ClientSource/DebugConsole.cs#L3779-L3833)、[发布选择](https://github.com/FakeFishGames/Barotrauma/blob/e6e95da83b1b5ca8b55ec52653c7cf6b40e30247/Deploy/DeployAll/Deployables.cs#L13-L23)、[publish 调用](https://github.com/FakeFishGames/Barotrauma/blob/e6e95da83b1b5ca8b55ec52653c7cf6b40e30247/Deploy/DeployAll/DotnetCmd.cs#L21-L37)
4. **对 Farm Exchange 的启发。** 可以借鉴“共享源码，平台和构建类型是配置维度”及 partial 拆工具职责。它的客户端/服务端、多平台项目和巨大静态命令类源于其规模与联机需求；Farm Exchange 当前不需要照搬这些结构，也不应为了调试把模块私有状态全部公开。这是本项目适配判断。

## 案例比较

| 案例 | 正常游戏是否另复制一套 | 工具访问方式 | 发行路径工具处理 |
| --- | --- | --- | --- |
| Nez | 否 | 静态命令、当前场景与实体 | DEBUG 省略主循环调用并排除内置命令/检查器；控制台本体仍在源码编译项 |
| Thrive | 否 | 工具 UI → 管理器事件 → 真实阶段模拟；部分全局状态 | ExportRelease 排除测试；调试/作弊工具仍编入，部分入口运行时设置控制 |
| Barotrauma | 否 | 命令委托 → 当前游戏对象 | 配置符号排除部分专项命令；控制台与普通作弊命令保留 |

共同可借鉴的是单份游戏状态、工具职责拆分、少量明确的生命周期接入。源码案例也说明：不维护两套经营代码，与是否在发行版保留工具，是两个独立决定；`partial` 是类的源码拆分，不自动带来编译隔离；工具是否编入要另外检查条件编译、编译项和资源导出配置。
