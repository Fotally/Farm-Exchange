# 自动化测试与性能测量

## 自动化测试与行覆盖率

测试按[测试分类调研](../research/test-taxonomy.md)分开存放：`tests/unit/` 检查独立的游戏状态、市场和地图坐标；`tests/integration/` 检查镜头输入与地图选择协作；`tests/e2e/` 检查主场景完整经营流程；`tests/performance/` 测满地图负载与可选图形 FPS。`tests/test_suite.tscn` 汇总各类可在 headless 模式运行的检查，任一失败都会返回非零退出码。满地图检查将 16,384 格全部放上现有实体（8,192 块农田、8,192 处加工场地，六种作物均覆盖），推进 50 tick 并打印总耗时与平均耗时。耗时是本机观测值，没有固定通过阈值。Windows 导出程序启动另作构建冒烟测试。

各文件都有独立的场景入口，排查时可用 `Godot控制台程序 --headless --path . 场景路径` 单独运行：

| 类别 | 场景路径 |
| --- | --- |
| 单元测试 | `tests/unit/test_farm_game.tscn`、`tests/unit/test_market_rules.tscn`、`tests/unit/test_world_map.tscn` |
| 集成测试 | `tests/integration/test_camera_interaction.tscn` |
| 端到端测试 | `tests/e2e/test_core_loop.tscn` |
| 满地图负载测试 | `tests/performance/test_full_world_load.tscn` |

在项目根目录执行完整测试：

```powershell
./tools/Run-Tests.ps1 -GodotConsole 'E:\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
```

脚本使用仓库锁定的 `dotnet-coverage` 版本生成 `coverage/coverage.cobertura.xml`。覆盖率只统计 `scripts/` 中的业务脚本，排除 `tests/` 和 Godot 自动生成源码；总行覆盖率不得低于 80%，本次迁移后的本地结果为 94.49%。修改业务功能时必须在同一 issue 中同步修改或补充对应测试；不得通过排除业务文件降低统计范围。覆盖率报告与 `coverage/` 目录不纳入 Git。

图形 FPS 性能测试是同一测试脚本的可选阶段。修改地图绘制、镜头、实体负载或渲染设置，或需要建立性能基线时运行；一般业务规则修改先以必跑的 headless 测试为准：

```powershell
./tools/Run-Tests.ps1 -GodotConsole 'E:\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe' -Performance
```

这会先完成 Debug 编译、headless 场景测试和覆盖率检查，再在可见窗口运行 `tests/performance/test_full_world_fps.tscn`，写出 `coverage/performance.json`、`coverage/performance.png` 和 `coverage/performance-end.png`。图形阶段使用真实主场景的地图、镜头和一秒 tick：满地图预热 2 秒，采样 8 秒，镜头在中心附近持续水平移动。报告记录绘制帧数、平均 FPS、帧间隔中位数与 P95/P99、可见地图块覆盖的最多候选格数、地图块重建次数、窗口尺寸、缩放、VSync、CPU、GPU 和引擎版本。图形测试要求平均 FPS 至少 60、P95 帧间隔不超过 16.67 ms，并应核对采样前后截图中的建筑和作物标记。它测量 Godot 编辑器 Debug 构建中的移动镜头负载；`--headless` 不会得到有效渲染帧。测得的 FPS 随机器与图形环境变化，当前合格线只针对本机验收条件。

2026-09-24 本机优化前基线：AMD Ryzen 7 5800H、NVIDIA GeForce RTX 3060 Laptop GPU、Windows 10.0.26200、Godot 4.7.2 Mono Debug；1280×720、1.25 倍缩放、VSync 关闭、FPS 不限，镜头水平往返移动。满地图采样 8.06 秒、122 帧，平均 **15.14 FPS**，帧间隔中位数 64.64 ms、P95 为 76.42 ms、P99 为 80.71 ms；旧绘制循环每帧最多处理 1,600 个候选格。同期必跑的 50 tick 逻辑检查耗时 13.95 ms，平均 0.279 ms/tick。

2026-09-25 本机分块网格缓存测量：同样的窗口、缩放、VSync 和镜头移动条件，满地图采样 8.00 秒、7,327 帧，平均 **915.78 FPS**，帧间隔中位数 0.97 ms、P95 为 1.53 ms、P99 为 2.05 ms；可见地图块最多覆盖 1,600 个候选格，采样期间重建 127 块。必跑的 50 tick 检查耗时 12.68 ms，平均 0.254 ms/tick。两组数据用于本机同条件对比，不代表所有硬件或 Release 成品。
