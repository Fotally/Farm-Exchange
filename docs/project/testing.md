# 自动化测试与性能测量

本页维护现行执行方法、测试入口和验收门槛。历次交付的覆盖率、性能数字与截图属于历史证据，不代表当前工作树的实测结果；按[归档索引](../archive/index.md)查阅。

## 自动化测试与覆盖率

建筑放置预览由 `tests/integration/test_placement_preview.tscn` 检查独立覆盖层、真实占用、局部冲突、越界、跨块、取消和镜头定位；`tests/unit/TestWorldMap.cs` 以 2×4 偏移数据验证同一几何的覆盖、阻塞与外围。`tests/e2e/test_build_placement.tscn` 使用主场景验证农田、七种加工场地和道路的逐次建造、统一取消、冻结资金与费用提示、暂停及原生鼠标跨界面输入。三组接入 `TestSuite`；独立有窗口运行摆放场景保存 `build/issue87-validation/valid.png`、`conflict.png`、`camera.png`、`edge.png`，地图预览场景另保存 `coverage/placement-*.png`。图像产物仅在有图形后端时生成。

满地图性能场景保留原夹具；追加 `-- --placement-preview` 用户参数时启用随当前镜头移动的候选预览负载，写入 `coverage/performance-placement.json` 和同前缀的前后截图。先运行普通场景进行同条件比较，再运行预览负载；两者均须满足平均至少 60 FPS、P95 帧间隔不超过 16.67 ms。预览不得增加地图块重建。

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

该工具只在 `build/` 生成独立控制台探针，引用指定发布程序集及随包依赖；报告保留实际载入 DLL 的 SHA-256，并核对与输入文件相同。正式 Windows 导出程序启动和同版本 schema 随包验收仍按[Windows 构建](windows-build.md)执行。本文描述可执行方法，不表示当前工作树已经通过上述门禁。
