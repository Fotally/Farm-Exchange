# 自动化测试与性能测量

本页维护现行执行方法、测试入口和验收门槛。历次交付的覆盖率、性能数字与截图属于历史证据，不代表当前工作树的实测结果；按[归档索引](../archive/index.md)查阅。

## 自动化测试与覆盖率

#35 批量加工由 `TestBatchProcessing` 经真实库存、加工与经营入口验证每批最多9份、少量批次完整周期、零原料、底线与冻结、稳定竞争领取、锁量不追加、拆除损失及实际入库守恒；单秒与批量推进必须一致，七作物产能检查使用明确数量和天气输入。`TestBatchProcessingUi.RunChecksAsync` 接入 `TestSuite`，使用真实主场景验证满批、少批、等待状态、暂停补料与拆除；有窗口独立运行 `tests/e2e/test_batch_processing_ui.tscn`，在1920×1080保存截图到 `build/issue35-ui-validation/`，检查批量、周期、损失说明和操作按钮完整可见。headless 检查行为和排版，不代替截图验收。

批量加工单元及1080P详情夹具显式使用合法纯陆地配置；主场景夹具等待 `ReadyTask` 完成后读取经营状态，原九份批量及满批／少批断言保持。

水域与道路导航由同一 TestSuite 验证：`TestTerrainRules` 检查严格配置，`TestTerrainMap` 覆盖正式多种子、全量工作中心、真实端点与连续取消，`TestLandPath` 检查累计距离和采样；`TestRoadNavigation` 使用真实原生区域成本、跨块复建、清空道路与取消释放，并通过 Godot Logger 将原生完整边冲突警告判为失败。`TestFarmGame` 和 `TestCoreLoop` 验证道路准备期间不消费经营时间、当前行程保持、最新道路重选以及主驱动检查点内建拆后的剩余 tick 停止；同步开发流程入口也不能绕过准备。

`TestFarmGame` 独立入口和 `TestSuite` 完整入口都捕获异步测试异常，记录完整堆栈并以退出码 1 结束。测试进程仍须实际退出；仅有最后一条输出或未退出的进程不能作为通过证据。

`tests/performance/test_water_fps.tscn` 必须使用真实 1920×1080 图形窗口运行，覆盖默认含水局、三类水纹逐像素、干地不变、暂停、有路／拆路真实工人对照及 0.5×／1×／2×／16×三工人跨图负载。筹资只使用真实收获出售。`test_road_fps.tscn` 保留45格道路和原采样窗口，在导航就绪后测量；两者继续采用平均至少60 FPS、P95不超过16.67 ms的门槛。报告和截图位于对应场景声明的 build 目录，不纳入 Git。

计划供水修复的回归继续由 `TestProductionState`、`TestProductionResults`、`TestCultivationPlanBook` 和 `TestWorkerScheduler` 的既有 TestSuite 入口运行。通过真实 FarmGame 的三田／四田、两种计划、手动预备、首次应用与重应用、雨水取消及单秒／批量对照，验证当前 Water 认领和成功 Sow 保持；旧待执行 Sow 禁播再启用不复活，真正重启、拆建、收获及禁生清理继续失效。实现见[农田工作身份](../architecture/farming/farming-system/implementation-work-identity-proposal.md)。

`TestCameraInteraction.RunResizeChecksAsync` 在真实 Main 中使用 GUI 焦点、公开 Input 事件和场景树自动处理帧，验证名称与原料保留数字编辑时镜头不动、释放／隐藏编辑后持键平移恢复及松键停止；受控 0.1 秒回调仅补充速度与焦点类型检查。另覆盖 WASD、Ctrl+A、多窗口、TextEdit、SpinBox、只读和按钮焦点、鼠标释放、尺寸往返及真实全屏。有窗口时等待实际绘制完成并保存 1920×1080 编辑截图；headless 输入注入不替代操作系统中文 IME 组合窗口验收。实现见[镜头输入](../architecture/world/camera-controller/implementation-editing-input-proposal.md)。

`tools/Test-ValidationArtifacts.ps1` 已接入 Windows CI 静态阶段，以真实受控进程和目录／ZIP输入验证启动成功、非零与 60 秒超时、证据保存失败保源、两次启动后分发目录及最终 ZIP 干净、schema 字节和其他依赖完整。FPS 契约覆盖成功、落盘后失败、部分／零产物、skipped、取消及未勾选，失败收集不改变原测量结论。可单独执行 `pwsh -NoProfile -File tools/Test-ValidationArtifacts.ps1`；Linux apphost 夹具不作为原生 Windows 游戏启动证据。完整操作与实际产物路径见[分发和性能证据](validation-artifacts-proposal.md)。

道路 #143 的领域检查接入 `TestFarmGame`、`TestLandOccupancy` 与 `TestBuildingLogging`，验证五材质不可变快照、整段重验、冻结资金、失败零修改、范围只拆道路和批量日志一次终结。`TestRoadMap` 调用 `TestRoadAppearance`（独立入口 `tests/integration/test_road_appearance.tscn`），验证全部16种连接 mask、稳定变体、混材质及跨8×8缓存块的正式／新建／拆除外观，以及移动、隐藏和取消后的恢复；有窗口运行同时检查实际纹理像素并保存截图。`tests/e2e/test_road_placement.tscn` 使用真实主场景与原生输入验证五材质未点击的单格起点预览、移动与隐藏恢复、只读状态及相邻旧路连接，再验证两次短按、两种L形轨迹、投影直线拆除、失败保段、两级取消、拖动与界面防穿透及暂停。上述检查均由 `TestSuite` 调用，原生产设施建造与旋转测试继续保留。道路图形夹具的正式准备与清理通过 Main 原有建拆操作，在截图前比较可见金币、冻结／可用金额、道路和设施计数与真实模型，暂停下也必须一致；仅同步地图不构成 UI 同步验收。

道路图形验收在 Debug 编译与导入后串行执行：先用 `tests/performance/test_full_world_fps.tscn` 保存同条件修改前后满地图报告和截图，再运行 `tests/performance/test_road_fps.tscn`，以1920×1080的真实主界面、五材质道路及移动新建／拆除预览测量帧率。平均至少60 FPS、P95帧间隔不超过16.67 ms；截图、报告和命令输出保存在 `build/issue143-validation/`，具体结果以当轮证据为准，不将未运行结果写成通过。

建筑放置预览由 `tests/integration/test_placement_preview.tscn` 检查独立覆盖层、真实占用、局部冲突、越界、跨块、取消和镜头定位；`tests/unit/TestWorldMap.cs` 以 2×4 偏移数据验证同一几何的覆盖、阻塞与外围。`tests/e2e/test_build_placement.tscn` 使用主场景验证农田、七种加工场地的逐次建造，以及道路工具接入、取消、冻结资金与费用提示、暂停及原生鼠标跨界面输入。三组接入 `TestSuite`；独立有窗口运行摆放场景保存 `build/issue87-validation/valid.png`、`conflict.png`、`camera.png`、`edge.png`，地图预览场景另保存 `coverage/placement-*.png`。图像产物仅在有图形后端时生成。

满地图性能场景保留原夹具；追加 `-- --placement-preview` 用户参数时启用随当前镜头移动的候选预览负载，写入 `coverage/performance-placement.json` 和同前缀的前后截图。先运行普通场景进行同条件比较，再运行预览负载；两者均须满足平均至少 60 FPS、P95 帧间隔不超过 16.67 ms。该农田候选覆盖层不得增加地图块重建；道路连接预览只允许局部刷新候选及四邻涉及的地图块，相同外观不重复重绘。

测试按[测试分类调研](../research/test-taxonomy.md)分开存放：`tests/unit/` 检查独立的经营状态、土地、生产、资源、行情、日历与坐标；`tests/integration/` 检查镜头、选择、角色与地图绘制，`tests/e2e/` 检查主场景流程，`tests/performance/` 测负载和图形 FPS。`tests/test_suite.tscn` 汇总 headless 检查，任一失败返回非零。满地图在 384×384 基础格中平铺 8,192 农田与 8,192 加工场地，每座占 3×3：共 16,384 生产实例、147,456 占用格，七作物均覆盖。50 个经营步进检查角落实例只推进正常一秒，并打印总/平均耗时；耗时无固定阈值。实体数与占用格分开报告，不把子格当产能。Windows 导出程序启动另作构建冒烟测试。

各文件都有独立的场景入口，排查时可用 `Godot控制台程序 --headless --path . 场景路径` 单独运行：

| 类别 | 场景路径 |
| --- | --- |
| 单元测试 | `tests/unit/test_farm_game.tscn`、`tests/unit/test_resources.tscn`、`tests/unit/test_production_state.tscn`、`tests/unit/test_land_occupancy.tscn`、`tests/unit/test_worker_scheduler.tscn`、`tests/unit/test_market_rules.tscn`、`tests/unit/test_market_quotes.tscn`、`tests/unit/test_trading_service.tscn`、`tests/unit/test_game_calendar.tscn`、`tests/unit/test_world_map.tscn` |
| 集成测试 | `tests/integration/test_camera_interaction.tscn`、`tests/integration/test_npc_preview.tscn`、`tests/integration/test_worker_presentation.tscn`、`tests/integration/test_road_map.tscn`、`tests/integration/test_placement_preview.tscn` |
| 端到端测试 | `tests/e2e/test_core_loop.tscn`、`tests/e2e/test_trade_orders_window.tscn`、`tests/e2e/test_build_placement.tscn` |
| 满地图负载测试 | `tests/performance/test_full_world_load.tscn` |

委托后端另由 `tests/unit/test_trade_orders.tscn` 验证条件、两种买单预算、冻结与释放、编辑失败零修改、原现金基准、1% 费用及经营相位；新单元和委托窗口端到端检查均接入根 `TestSuite`。有图形后端单独运行委托窗口场景时保存 `build/issue36-validation/orders-window.png`，用于检查真实中文布局；headless 检查不请求截图。

季节耕作表由 `tests/unit/test_cultivation_plan_book.tscn` 验证共享年度配置、首尾环绕、两种模式、完成时间缓存、同条一轮、手动接管和实际换季入库；农田状态测试另外覆盖禁生边界的精确阈值与防重复收获。`tests/e2e/test_cultivation_window.tscn` 使用真实主场景与原生输入验证作物拖入、跨季片段移动、批量引用、冲突与风险、草稿焦点和滚动布局。两组接入根 `TestSuite`；有图形后端运行窗口场景时保存 `build/issue42-validation/cultivation-window.png`，用于查看四季条和中文操作布局，headless 不请求截图。现行操作见[年度表说明](../gameplay/production/seasonal-cultivation-ui.md)，历史验收由[归档索引](../archive/index.md)定位。

在项目根目录执行完整测试：

```powershell
./tools/Run-Tests.ps1 -GodotConsole 'E:\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
```

脚本在 Debug 编译后先让 Godot 导入图片，再使用仓库锁定的 `dotnet-coverage` 版本生成 `coverage/coverage.cobertura.xml`。覆盖率只统计 `scripts/` 中的业务脚本，排除 `tests/` 和 Godot 自动生成源码。以 `scripts/` 的每个一级目录为模块，**每个模块及业务脚本总体的行覆盖率均不得低于 80%**；高覆盖率模块不能抵消未达标模块。`Run-Tests.ps1` 调用统一的 `Test-Coverage.ps1`，动态发现一级目录，按文件与行号去重、同一行任一记录命中即覆盖，自动输出并检查各模块与总体的有效行、已覆盖行及百分比；任一项低于 80% 或缺少有效数据即失败。本地与 CI 共用该门禁，不再要求人工另算模块结果。路径口径、缺失检查及独立夹具命令见[自动覆盖率门禁](../static-checks/coverage.md)。修改业务功能时必须在同一 issue 中同步修改或补充对应测试；不得通过排除业务文件降低统计范围。覆盖率报告与 `coverage/` 目录不纳入 Git。

手动勾选 performance 时，CI 在普通场景与覆盖率成功后独立调用 `Run-PerformanceTests.ps1`；本地 `Run-Tests.ps1 -Performance` 仍在同一前置完成后调用相同命令。测量开始清理本次三个固定输出，成功和失败均由显式状态条件收集实际 `performance.json`、`performance.png`、`performance-end.png` 及缺失清单，原阈值失败继续使 job 失败。取消后跳过收集；前置失败未开始测量时不生成性能产物。artifact 名包含提交、run-id 和 attempt，保留 14 天。

## 日志专项验收

先完成 Debug 编译和资源导入，再串行执行下列工具。工具不重编正式项目、不修改采集实现，每次指定不存在的输出目录以防混入旧证据；普通 `-Performance` 仍使用原 8 秒满图场景。详细用例、故障含义与 Release 门禁见[日志验收实现](../architecture/logging/implementation-validation.md)。

```powershell
./tools/Run-LoggingPerformance.ps1 -GodotConsole 'E:\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
./tools/Test-LoggingLifecycle.ps1 -GodotConsole 'E:\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
```

图形矩阵每格独立进程、种子 130、1920×1080、预热 2 秒、采样 65 秒，覆盖满图 1×/16×下的关闭、runtime、development、持续 File 目标故障四档，以及连续交易和第二局组合负载、development 有界诊断。平均至少 60 FPS、P95 帧间隔不超过 16.67 ms；失败也保留已有报告。16×是本轮确认的最高压测倍率请求，报告同时给出实际推进量和吞吐，不能视作维持该倍率的承诺。首张截图完成后重新建立帧、托管分配及 GC 基线，末张截图在采样后保存。

`matrix.json` 汇总所有格，`differences.json` 只比较存在关闭基线的同负载格；每格 `report.json`、`before.png`、`after.png`、实际日志和 `process.log` 独立保存。托管分配使用 `GC.GetTotalAllocatedBytes(true)`，三代 GC 次数分别记录，不包含 Godot/GPU 原生内存。日志事件按 SessionId/Sequence 去重；失败计数不代表精确丢失事件数。

16×有界格的本次请求预算为 500 条，报告字段 `RequestedCaptureEventLimit` 明确记录该值；公共硬上限仍为 5000 条。该格必须在关局前真实按 `EventLimit` 停止且实际采集 500 条，未达到时报告无效；不声称本格实测了 5000 条。

Release 编译与 Windows 正式导出后，分别对实际 DLL 执行探针，不以 Debug 下选择 runtime 档代替发布验证：

```powershell
./tools/Test-LoggingRelease.ps1 -AssemblyPath .godot/mono/temp/bin/Release/FarmExchange.dll -OutputDirectory build/issue130-validation/release-probe
./tools/Test-LoggingRelease.ps1 -AssemblyPath build/windows/data_FarmExchange_windows_x86_64/FarmExchange.dll -OutputDirectory build/issue130-validation/export-release-probe
```

该工具只在 `build/` 生成独立控制台探针，引用指定发布程序集及随包依赖；报告保留实际载入 DLL 的 SHA-256，并核对与输入文件相同。控制台使用 `System.IO` 读取仓库正式天气 JSON，经 `WeatherRules.Parse` 验证后显式传入经营入口，报告记录配置路径与 SHA-256；缺失或无效配置直接失败。正式 Windows 导出程序启动和同版本 schema 随包验收仍按[Windows 构建](windows-build.md)执行。本文描述可执行方法，不表示当前工作树已经通过上述门禁。

## 地区天气与随机产量验收

#34 的真实天气规则见[地区天气](../gameplay/world/weather.md)，预排及显式设置约定见[天气 Interface](../architecture/weather/interface-weather-system.md)。天气专用单元测试与地图表现集成测试接入根 `TestSuite`。配置解析与真实排程共用正式路径，追加同进程合法配置变体隔离及局组装验证；土地与天气生产者的稳定快照引用、变更失效和旧快照不变性均由行为测试保护。农田推进显式传入查询结果，包括晴天。天气配置变体按JSON字段设置、移除或重命名，紧凑与缩进输入均走正式解析及实际执行路径，保持各变体隔离。现有场景继续覆盖自然预排的完整存续区间和循环接缝、区域失败零修改、跨界九格优先级、同种子单秒／批量一致、整轮天气加权及最后不足一个经营秒的精确时间累计。计划、底线、日志和生产结果的旧固定产量断言改为真实收获及资源守恒，基准定义检查仍保留七个原数值。

图形验收在完成 Debug 编译与导入后串行运行既有满地图 FPS 场景，以及天气专用场景；天气场景使用显式测试输入和真实主界面，不提供玩家天气控制。各倍率使用独立进程，基准1920×1080：

```powershell
& 'E:\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe' --path . tests/performance/test_weather_fps.tscn -- --weather-rate 1
```

分别将倍率参数设为 `0.5`、`1`、`2`、`16`，检查雨点在真实雨区、可见部分裁切、暂停冻结、高倍率密度及详情“当前天气／基准产量”。场景按倍率分别保存到 `coverage/weather-0-5x/`、`weather-1-0x/`、`weather-2-0x/`、`weather-16-0x/`，每个目录包含 `before-sunny.png`、`after-cloud-rain.png`、`end-cloud-rain.png` 与 `performance.json`；截图过程不计入帧采样。平均至少60 FPS、P95帧间隔不超过16.67 ms，实际结果与失败证据均保留；本页描述执行要求，不把尚未运行的数字写成通过。总体及每个一级模块的80%行覆盖率门槛同样覆盖新增 `scripts/weather/`。
