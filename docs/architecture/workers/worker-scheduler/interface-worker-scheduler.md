# WorkerScheduler 对外接口

内部可选 `Diagnostics` 由经营局组装为本局 `WorkerDiagnostics`。诊断先按工人及事件门禁，再在实际认领、释放、开始行程、抵达、播种转供水处记录，不把整秒前后比较代替中间转换。日志目标来自 `FarmWorkRequest.CellIndex` 的锚点，而公开快照 TargetCell 保持工作中心含义。quiet 行程只记录原累计区间的一次位置前后，关闭或故障不改变任务和移动；详见[生产与工人诊断](../../logging/implementation-production-diagnostics.md)。

对应类型：`FarmExchange.Workers.WorkerScheduler`，代码位于 `scripts/workers/WorkerScheduler.cs`。它唯一维护经营工人的位置、当前任务、移动时间、田块认领关系与共用轮转游标；不拥有作物、水分或库存。新游戏构造三名真实经营工人；移动与预算规则见[实现说明](implementation-movement-proposal.md)。

| 成员 | 约定 |
| --- | --- |
| `WorkerScheduler(TerrainReadView terrain, params Vector2I[] startingCells)` | 接收完整就绪的本局不可变地形，根据起始格坐标创建工人，编号从 1 开始，初始空闲、无目标。生产传入工作中心 (190,190)、(193,190)、(196,190)；不保留调用方的可变数组，至少需要一名工人。 |
| `AdvanceOneSecond(FarmingSystem farming, CalendarSnapshot calendar)` | 在 `FarmGame` 的既有工人相位调用一次，所有工人各推进完整 1 秒；先统一清理失效认领，再按编号推进。至少一人完成播种或浇水时返回 `true`，单纯移动返回 `false`。暂停快照使本次调用零修改并返回 `false`。 |
| `GetSnapshots()` | 返回按编号排列的独立只读 `IReadOnlyList<WorkerSnapshot>`，不推进经营，也不暴露可变工人状态。旧快照不随后续推进改变。 |

`WorkerSnapshot` 为公开只读值，位于 `scripts/workers/WorkerSnapshot.cs`：`WorkerNumber` 为从 1 起的固定编号，`GridPosition` 是浮点基础格坐标，`TargetCell` 是可空整数工作中心格，由农田锚点经 `BuildingFootprint.WorkCell` 派生，`Activity` 为 `Idle`、`Moving`、`Sowing` 或 `Watering`。空闲时目标为空且位置保持；移动最后一秒落到目标后立即显示 `Sowing`/`Watering`，该秒不执行工作。干田播种完成立即显示 `Watering`，供水完成或湿田播种完成立即空闲。状态表示下一步将继续的工作，不是额外动画回调。

移动先向已就绪的原生地形导航查询从实际连续位置至工作中心的完整陆地路线，成功后才认领并推进游标；正常不可达跳过目标，生成或技术计算失败直接传播，不冒充成功。每段距离为最大轴差（Chebyshev），整趟累计后以每模拟秒 3 基础格一次向上取整；零距离无移动耗时，最后不足三格仍独占一秒。内部持有 `LandPosition` 的有限精度 double 坐标、不可变 `LandPath` 和整数累计已行秒，以 `Path.GetPositionAtDistance(TravelTicks * 3)` 采样，不逐段取整、不从 `Vector2` 投影回读。取消不改变已记录的位置，新路线从该连续点接续，不再保存任意精度有理分母。播种与浇水各需完整一秒；到达、播种、浇水之间不能重复使用同一秒。圆脚底半径 0.125 格，Round 导航使用已批准的 1/256 格数值安全余量、1/32 格保守简化与额外 1/8 格离岸退让；不承诺任意略宽于 0.25 格的通道必然可达。工人可穿越建筑与其他工人，不要求道路，也无道路速度加成。

快照的 `ExactPosition` 保留既有字段名，表示权威 double 连续位置，并不承诺无限精度；`GridPosition` 仅为显示投影。`Path`、`TravelTicks` 描述当前完整行程。`Movement` 仅包含最近完整经营秒的实际移动：同一路线、完成秒戳、距离区间、目标工作中心与原动作，并在内部原样保存 FarmingSystem 生成的完整不透明工作凭据。外部不读取或重建轮次与播种许可计数；`FarmGame.IsWorkerMovementCurrent` 比较同一凭据，因此休耕后恢复播种不会复活旧行程，同轮供水仍保留。平静批量仍只公布尾部最后一秒，不能把整个批量区间当作一秒播放。下一秒没有移动时清空记录，暂停零修改；表现暂停应停止播放并对齐真实位置。路径按实际 double 点值相等，快照比较不受路线对象引用影响。旧快照不可变。外部命令可能先于下一工人相位取消任务，表现用 `FarmGame.IsWorkerMovementCurrent` 读取当前农田凭据立即重验。

调度通过 [FarmingSystem](../../farming/farming-system/interface-farming-system.md) 查询真实工作需求并提交指定动作。当前私有 `SelectNextWork` 采用共享锚点索引轮转，只扫描 `FarmingSystem.Indices` 缓存中的真实实例，跳过已认领或无需求的田；选中时推进游标并独占认领。认领覆盖移动、播种和紧接着的原地浇水，完成或失效时释放。雨水已满足浇水、强制改种重启、拆除同格重建、收获新轮或越季清理使旧任务失效；单纯播种启停仅作废旧待执行 `Sow`，同轮需水仍存在时保留 `Water` 及原认领。执行前还要用同一日历快照重新检查，失败不得改成另一种动作。

统一播种规则由农田需求表达：当前适季且启用播种的空田提供播种需求，预计时间不足仅提示风险；禁生季节和休耕空田无播种需求。禁播不影响已播种本轮继续浇水生长；计划播后关闭复种、手动预备下一轮或首次应用计划都保留当前轮的原工人供水续接。禁播后再启用仍须重新领取 `Sow`，不能复活禁播前的播种任务。手动立即改种调用 `RestartCrop`，同品种也强制中断本轮并失效旧任务；兼容内部 `SetCrop` 的同品种无变化与重复设置相同启停值不改版本。工人不拥有共享耕作表或预备安排，也不读取农田轮次与许可计数。

调用方只知道一秒推进与只读快照，不传游标、认领容器、优先级或策略类型。未来复杂选择算法首先替换内部 `SelectNextWork`，继续使用农田需求与凭据，不把算法铺开到 `FarmGame`/UI；当前不创建 C# 策略接口、注册器或额外 Adapter。工人之间任务数无需相等，保证规模不是每人固定田区，也不是禁止继续建造的数量上限。

移动耗时和方向集中在私有 `BeginWork`，每秒位置推进集中在私有 `AdvanceTravel`。本批固定三基础格每秒，道路偏好由本局 TerrainMap 的 Godot 原生区域成本提供：道路 1、普通陆地 2，所有材质同等；WorkerScheduler 不持有道路副本或另做搜索。外部一秒推进与快照不传道路类型或速度乘法，UI/NPC 不重新计算移动。不预建道路策略或恒返回 1 的速度 Module。

测试入口为 `tests/unit/workers/TestWorkerScheduler.cs` 与 `tests/unit/workers/test_worker_scheduler.tscn`，覆盖秒不重用、三人独占与连续工作、任务失效及先释放全部认领、暂停与确定性、分数格路线、季末重验、单人 8 田与三人 24 田预算及至少三轮自动复种。运行通过状态以集中验收记录为准，方案中的预算不是实测证明。

预算测试分别建立冷启动连续三轮，以及已在工作时其他田就绪两种夹具。后者先只建 1/3 块田，让 1/3 名工人各推进一秒，并断言全部已有认领，再建同一稳定矩形内其余田；逐田从真实就绪时刻计到供水，检查 45/81 秒。不能把连续复种是否偶然碰上已有工作当作这一项覆盖的保证。

预算布局按一个标准田跨度＝三基础格换算；单人 8 田、三人 24 田与 40/45/72/81 秒上界保持。测试以标准田坐标描述预算夹具，统一换算真实锚点和工作中心；另用任意基础格锚点验证分数位置、末段不足三格向上取整和集合增删后的稳定游标。

## 批量行程

`GetNextEventSeconds(farming, calendar)` 在本模块查询任务失效、空闲工人可认领、到达及动作边界；无事件为 `uint.MaxValue`，任务选择/失效/原地动作为下一秒。`AdvanceQuietSeconds(seconds, elapsedSeconds)` 接收区间开始前的经营秒，只推进严格位于下一事件之前的有效行程，不选择任务、不动作。单秒与批量使用同一完整路线与整数累计秒采样，并发布相同的最近秒区间。完整相位和比较测试见[批量实现](../../game-state/farm-game/implementation-batched-simulation.md)。

## 真实作业完成输出

`AdvanceOneSecond(farming, calendar, completed=null)` 的可选完成回调由 FarmGame 在已有工人相位提供，参数为工人编号与原 `FarmWorkRequest`。只有 `TryCompleteWork` 成功后调用一次；移动、准备、失败重验、降雨使供水不再需要均不调用。返回 bool、认领释放、播种后同田供水与经营顺序保持。回调只汇集该经营秒结果，不修改任务或农田；表现不能订阅调度并驱动业务。`WorkerSnapshot.Activity` 继续代表下一步任务，不能代替此成功输出。

道路变化后的经营宿主须先等待 [FarmGame 导航就绪](../../game-state/farm-game/interface-farm-game.md)。新领取或任务失效后的重新规划使用最新已同步道路成本；已领取的不可变 `LandPath`、原始工作凭据与累计行程不因道路建拆重置。准备耗时不消费经营秒，纯陆地有路也遵守此约定。道路不保证每次贴近所有短路，不提供速度加成；正常无路陆地仍可选择。
