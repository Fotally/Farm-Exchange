# FarmingSystem 对外接口

对应类型：`FarmExchange.Farming.FarmingSystem`，代码位于 `scripts/farming/FarmingSystem.cs`。它唯一维护每块农田的所选作物、水分、播种或生长阶段、剩余精确时间单位和本实例工作凭据版本；状态数组只有锚点有值，九个子格不复制状态。空地与加工格不创建农田状态。

| 成员 | 约定 |
| --- | --- |
| `Place(index, crop)`、`Remove(index)` | 在 `FarmGame` 的建造、拆除协调中创建或清理该锚点农田状态。 |
| `Indices` | 返回现有农田锚点升序的独立只读缓存；建造、拆除、清空后重建缓存，旧集合不随之改变。调度只扫描真实农田，轮转游标仍按锚点索引保存，不按集合位置保存。 |
| `SetCrop(index, crop)` | 同品种无变化；改种时丢弃未收获进度并清零阶段与剩余时间，保留田块水分。 |
| `RestartCrop(index, crop)` | 强制丢弃本轮，包括同品种；清零阶段和剩余时间，保留水分与当前播种启停。用于手动立即改种及计划切换。 |
| `SetSowingEnabled(index, enabled)` | 控制空田是否可播种，默认允许；改变值使旧工作凭据失效。禁播不妨碍已播种一轮的供水与生长，恢复后必须重新获取凭据。 |
| `Get(index)` | 返回独立只读 `FarmSnapshot`，含作物、阶段、向上取整的 `RemainingSeconds`、`HasWater`、精确 `RemainingTimeUnits` 和 `SowingEnabled`。生长进度仍唯一归属本模块。 |
| `GetExpectedRoundEndTimeUnits(index, now)` | 只读预测本轮结束的累计比例单位：生长中按精确剩余量向上对齐实际经营 tick，再与首个禁生季起点取早；空田或待水返回空。`now` 为本步推进后的累计单位。适季跨季不截短；禁生边界可能促熟或清理，本结果不承诺收成、不修改农田、不猜测供水。计划直接读取本结果，不复制生长或季节结局规则。 |
| `SupplyWater(index)` | 雨水与工人浇水共用的一次供水操作；空田留水、已播种作物开始生长，生长中重复调用不重置进度。 |
| `AdvanceGrowth(index, out harvestedCrop)` | 每经营秒推进生长中的农田；成熟时清除本轮阶段和水分，并报告需入库的作物。收获数量由作物定义决定。 |
| `TryMatureBeforeDisallowedSeason(index, season, out harvestedCrop)` | 仅由经营换季相位调用，当前生长中作物在传入季节禁生且精确剩余时间严格小于完整周期的 10% 时，共用正常收获终结并报告作物，入库仍由 `FarmGame` 处理。其他阶段、适季跨季及等于或超过阈值返回 `false` 且零修改；同轮不能重复报告收成。 |
| `GetWorkNeed(index, calendar)` | 返回可空只读 `FarmWorkRequest`：空田、启用播种且当前适季时需 `Sow`，干燥已播种田需 `Water`；无田、生长中、休耕空田或不适季返回空。预计时间不足仅提示风险，仍可播种。只查询、不认领或修改农田；需求不包含工人优先级、游标或路线成本。 |
| `TryCompleteWork(request, calendar)` | 使用执行时日历重新查询需求，并逐项匹配目标锚点、本实例版本与指定动作；不匹配返回 `false` 且零修改。有效 `Sow` 使农田进入已播种，已有水则直接生长；有效 `Water` 调用同一 `SupplyWater` 后生长。不会把过期浇水转为播种。 |
| `TryWork(index, calendar)` | 兼容已有农田模块调用：查询当前需求后立即转发 `TryCompleteWork`，不保留第二套规则。真实经营工人通过需求、移动耗时及指定动作执行，不用本方法跳过移动或工作秒。 |
| `ClearDisallowedCrops(season)` | 换季后检查全部现有农田；只清除新季节不适宜的待水或生长中作物，清零阶段、精确进度与水分，保留田块和所选作物。空田留水及适季作物保持不变；重复调用不会重复失败或产生收成。 |
| `SetGrowingForBenchmark`、`Clear` | 仅供满地图 50 tick 夹具设置和重建。 |

`FarmWorkRequest` 为本模块文件内的只读记录，包含锚点索引 `CellIndex`、`Revision` 与 `FarmWorkKind`（`Sow`/`Water`）。局部版本在新建、拆除、改种重启、播种启停发生变化、收获进入新轮、越季清理以及夹具清空时失效，避免同锚点同品种重建或新轮误接旧任务；兼容 `SetCrop` 的同品种无变化及重复设置同一启停值不改版本。播种后的紧接浇水仍属于同一版本。雨水不靠改版本取消任务，而是改变真实需求：已开始生长就不再需要人工浇水。该凭据不是全局实体标识，也不承诺调度优先级。

`WorkerScheduler` 只查询工作需求并调用指定工作操作，不取得可变农田状态。`FarmGame` 在步进开始时向全部现有农田实例各一次传递显式降雨，再按固定相位把正常成熟结果交给 `Inventory`；换季时先报告并沿原路径入库补救成熟，随后清理禁生未成熟作物，并由计划更新后续安排。调用方不逐字段清除农田，也不记录历史失败状态；已收获空田不会被越季清理。移除农田删除其水分状态。精确阈值与共同终结路径见[换季成熟实现](implementation-season-maturity.md)，播种检查见[PlantingRules 接口](../planting-rules/interface-planting-rules.md)，工人移动与三人认领见[WorkerScheduler 接口](../../workers/worker-scheduler/interface-worker-scheduler.md)。旧对象、旧轮、雨水满足及季末执行重验的专项测试位于 `tests/unit/TestWorkerScheduler.cs`。

模块不解析占用子格或决定设施形状；`FarmGame` 在土地接口解析锚点后调用，`WorkerScheduler` 从 `Indices` 取得真实锚点。少量容量测试数组直接验证生产状态，完整空间几何由土地测试负责。

批量内部接口 `GetNextEventSeconds(index)` 返回正常成熟的剩余完整秒数，未生长返回 `uint.MaxValue`；`AdvanceQuietSeconds(index, seconds)` 只累计严格位于成熟之前的区间，一次扣除 `7×seconds` 精确单位，不收获或更改凭据。换季仍是经营入口独立事件，促熟与清理沿用原操作。单秒与批量都在正常到期的事件秒调用原 `AdvanceGrowth`，不会对跨轮总时长统一取整。调用顺序见[批量实现](../../game-state/farm-game/implementation-batched-simulation.md)。
