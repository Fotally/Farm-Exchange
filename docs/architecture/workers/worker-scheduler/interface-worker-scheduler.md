# WorkerScheduler 对外接口

对应类型：`FarmExchange.Workers.WorkerScheduler`，代码位于 `scripts/workers/WorkerScheduler.cs`。它唯一维护经营工人的位置、当前任务、移动时间、田块认领关系与共用轮转游标；不拥有作物、水分或库存。当前 [#40/#43 已确认实施方案](implementation-movement-proposal.md)要求新游戏真实构造三名工人。

| 成员 | 约定 |
| --- | --- |
| `WorkerScheduler(params Vector2I[] startingCells)` | 根据起始格坐标创建工人，编号从 1 开始，初始空闲、无目标。生产传入工作中心 (190,190)、(193,190)、(196,190)；单工人预算测试可传入一格。不保留调用方的可变数组，至少需要一名工人。 |
| `AdvanceOneSecond(FarmingSystem farming, CalendarSnapshot calendar)` | 在 `FarmGame` 的既有工人相位调用一次，所有工人各推进完整 1 秒；先统一清理失效认领，再按编号推进。至少一人完成播种或浇水时返回 `true`，单纯移动返回 `false`。暂停快照使本次调用零修改并返回 `false`。 |
| `GetSnapshots()` | 返回按编号排列的独立只读 `IReadOnlyList<WorkerSnapshot>`，不推进经营，也不暴露可变工人状态。旧快照不随后续推进改变。 |

`WorkerSnapshot` 为公开只读值，位于 `scripts/workers/WorkerSnapshot.cs`：`WorkerNumber` 为从 1 起的固定编号，`GridPosition` 是浮点基础格坐标，`TargetCell` 是可空整数工作中心格，由农田锚点经 `BuildingFootprint.WorkCell` 派生，`Activity` 为 `Idle`、`Moving`、`Sowing` 或 `Watering`。空闲时目标为空且位置保持；移动最后一秒落到目标后立即显示 `Sowing`/`Watering`，该秒不执行工作。干田播种完成立即显示 `Watering`，供水完成或湿田播种完成立即空闲。状态表示下一步将继续的工作，不是额外动画回调。

移动沿当前格位置至目标格中心的直线，横纵及斜向速度均为每模拟秒 3 基础格（一个标准田跨度），耗时为 `ceil(max(abs(dx),abs(dy)) / 3)`；同格无移动耗时，最后不足三基础格的移动仍独占一秒。取消任务保留实际位置，新路线可从分数格开始。播种与浇水各需完整一秒；到达、播种、浇水之间不能重复使用同一秒。工人可穿越占用，彼此不碰撞，不读取道路连通或速度加成。

调度通过 [FarmingSystem](../../farming/farming-system/interface-farming-system.md) 查询真实工作需求并提交指定动作。当前私有 `SelectNextWork` 采用共享锚点索引轮转，只扫描 `FarmingSystem.Indices` 缓存中的真实实例，跳过已认领或无需求的田；选中时推进游标并独占认领。认领覆盖移动、播种和紧接着的原地浇水，完成或失效时释放。雨水已满足浇水、实际改种、拆除同格重建、收获新轮或越季清理使旧任务失效；执行前还要用同一日历快照重新检查，失败不得改成另一种动作。

调用方只知道一秒推进与只读快照，不传游标、认领容器、优先级或策略类型。未来复杂选择算法首先替换内部 `SelectNextWork`，继续使用农田需求与凭据，不把算法铺开到 `FarmGame`/UI；当前不创建 C# 策略接口、注册器或额外 Adapter。工人之间任务数无需相等，保证规模不是每人固定田区，也不是禁止继续建造的数量上限。

移动耗时和方向集中在私有 `BeginWork`，每秒位置推进集中在私有 `AdvanceTravel`。本批固定三基础格每秒，道路仅作布局；未来确认道路速度或属性后，由组装提供必要的只读土地信息，优先在这些内部位置调整。外部一秒推进与快照不传道路类型或速度乘法，UI/NPC 不重新计算移动。不预建道路策略或恒返回 1 的速度 Module。

测试入口为 `tests/unit/TestWorkerScheduler.cs` 与 `tests/unit/test_worker_scheduler.tscn`，覆盖秒不重用、三人独占与连续工作、任务失效及先释放全部认领、暂停与确定性、分数格路线、季末重验、单人 8 田与三人 24 田预算及至少三轮自动复种。运行通过状态以集中验收记录为准，方案中的预算不是实测证明。

预算测试分别建立冷启动连续三轮，以及已在工作时其他田就绪两种夹具。后者先只建 1/3 块田，让 1/3 名工人各推进一秒，并断言全部已有认领，再建同一稳定矩形内其余田；逐田从真实就绪时刻计到供水，检查 45/81 秒。不能把连续复种是否偶然碰上已有工作当作这一项覆盖的保证。

#75 将预算布局按一个标准田跨度＝三基础格换算；单人 8 田、三人 24 田与 40/45/72/81 秒上界保持。测试以标准田坐标描述预算夹具，统一换算真实锚点和工作中心；另用任意基础格锚点验证分数位置、末段不足三格向上取整和集合增删后的稳定游标。
